using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestrator for all projectile behavior. Reads SpellTags and wires up
/// modular ProjectileBehaviorBase components at Init time. Core responsibilities:
///   - PROBABILITY resolution (picks one behavior tag per cast)
///   - AddBehaviors() — AddComponent per relevant tag
///   - HitEnemy()     — damage, status, force, then callbacks to behavior components
///   - Default wall-destruction for non-bounce projectiles
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class ProjectileHandler : MonoBehaviour
{
    [SerializeField] private GameObject aoePrefab;

    // ── State ────────────────────────────────────────────────────────────────
    private ProjectileContext ctx;
    private bool              initialized;
    private int               hitCount;
    private readonly HashSet<GameObject> hitEnemies = new();

    private const int   MaxPierceHits = 5;
    private const float Lifetime      = 5f;
    private const int   WallLayer     = 1 << 9;

    // ── Init ─────────────────────────────────────────────────────────────────

    public void Init(SpellData spell, Vector2 dir,
                     bool  isSplitChild    = false,
                     bool  isGhost         = false,
                     float damageMultiplier = 1f)
    {
        var rb      = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;

        // Resolve PROBABILITY before anything else
        HashSet<SpellTag> effectiveTags = ResolveEffectiveTags(spell);

        // Locate player
        GameObject playerObj   = GameObject.FindGameObjectWithTag("Player");
        Health     playerHealth = playerObj != null ? playerObj.GetComponent<Health>() : null;
        Transform  casterTf    = playerObj != null ? playerObj.transform : null;

        // Apply optional damage multiplier (e.g. SACRIFICE boost)
        SpellData activeSpell = spell;
        if (!Mathf.Approximately(damageMultiplier, 1f))
        {
            SpellData boosted  = ScriptableObject.CreateInstance<SpellData>();
            boosted.spellName  = spell.spellName;
            boosted.flavor     = spell.flavor;
            boosted.tags       = spell.tags;
            boosted.damage     = spell.damage * damageMultiplier;
            boosted.speed      = spell.speed;
            boosted.cooldown   = spell.cooldown;
            boosted.element    = spell.element;
            boosted.isMerged        = spell.isMerged;
            boosted.mergedFrom      = spell.mergedFrom;
            boosted.projectileColor = spell.projectileColor;
            boosted.secondaryColor  = spell.secondaryColor;
            boosted.projectileScale = spell.projectileScale;
            boosted.glowSize        = spell.glowSize;
            boosted.trailLength     = spell.trailLength;
            boosted.trailWidth      = spell.trailWidth;
            boosted.burstCount      = spell.burstCount;
            activeSpell             = boosted;
        }

        ctx = new ProjectileContext(activeSpell, effectiveTags, rb, playerHealth, casterTf, dir, this);

        // Ghost: invisible but fully damaging
        if (isGhost)
        {
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }
        else
        {
            ApplyVisuals(activeSpell);
        }

        // Set initial velocity
        rb.linearVelocity = dir * activeSpell.speed;

        // DELAYED: sit dormant for 1.2s before activating
        if (ctx.HasTag(SpellTag.DELAYED))
        {
            rb.linearVelocity = Vector2.zero;
            var col = GetComponent<Collider2D>();
            if (col != null) col.enabled = false;
            Invoke(nameof(Activate), 1.2f);
        }

        // Wire up behavior components
        AddBehaviors(isSplitChild);

        // Lifetime — PERSISTENT spells never auto-destroy
        if (!ctx.HasTag(SpellTag.PERSISTENT))
            Destroy(gameObject, Lifetime);

        initialized = true;
    }

    private void Activate()
    {
        if (ctx == null) return;
        ctx.Rb.linearVelocity = ctx.InitialDirection * ctx.Spell.speed;
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;
    }

    // ── PROBABILITY resolution ────────────────────────────────────────────────

    private static HashSet<SpellTag> ResolveEffectiveTags(SpellData spell)
    {
        var all = new HashSet<SpellTag>(spell.tags ?? System.Array.Empty<SpellTag>());
        if (!all.Contains(SpellTag.PROBABILITY)) return all;

        // Collect all "behavior" tags (not movement / status / corruption)
        var candidates = new List<SpellTag>();
        foreach (var t in all)
            if (IsBehaviorTag(t)) candidates.Add(t);

        // Randomly keep exactly one; drop the rest
        if (candidates.Count > 1)
        {
            var chosen = candidates[Random.Range(0, candidates.Count)];
            foreach (var t in candidates)
                if (t != chosen) all.Remove(t);
        }

        return all;
    }

    private static bool IsBehaviorTag(SpellTag t) => t switch
    {
        SpellTag.CHAIN           => true,
        SpellTag.FRAGMENTING     => true,
        SpellTag.SPLIT_ON_IMPACT => true,
        SpellTag.AOE_BURST       => true,
        SpellTag.LINGERING       => true,
        SpellTag.SWAPPING        => true,
        SpellTag.CONTAGIOUS      => true,
        SpellTag.DETONATING      => true,
        SpellTag.BURROWING       => true,
        SpellTag.PUSH            => true,
        SpellTag.PULL            => true,
        _ => false,
    };

    // ── Behavior wiring ───────────────────────────────────────────────────────

    private void AddBehaviors(bool isSplitChild)
    {
        // ── Trajectory ──
        if (ctx.HasTag(SpellTag.HOMING) || ctx.HasTag(SpellTag.ENEMY_HOMING))
            AddBehavior<HomingBehavior>();
        if (ctx.HasTag(SpellTag.SPIRAL))
            AddBehavior<SpiralBehavior>();
        if (ctx.HasTag(SpellTag.STUTTER_MOTION))
            AddBehavior<StutterMotionBehavior>();
        if (ctx.HasTag(SpellTag.WALL_BOUNCE) || ctx.HasTag(SpellTag.REFLECTING))
            AddBehavior<WallBounceBehavior>();
        if (ctx.HasTag(SpellTag.BOOMERANG))
            AddBehavior<BoomerangBehavior>();
        if (ctx.HasTag(SpellTag.SENTIENT))
            AddBehavior<SentientBehavior>();
        if (ctx.HasTag(SpellTag.CHANNELED))
            AddBehavior<ChanneledBehavior>();
        if (ctx.HasTag(SpellTag.TETHERED))
            AddBehavior<TetheredBehavior>();
        if (ctx.HasTag(SpellTag.DELAYED_ARC))
            AddBehavior<DelayedArcBehavior>();
        if (ctx.HasTag(SpellTag.SKIPPING))
            AddBehavior<SkippingBehavior>();
        if (ctx.HasTag(SpellTag.SURFACE_CRAWLING))
            AddBehavior<SurfaceCrawlingBehavior>();
        if (ctx.HasTag(SpellTag.BURROWING))
            AddBehavior<BurrowingBehavior>();
        if (ctx.HasTag(SpellTag.PHASING))
            AddBehavior<PhasingBehavior>();

        // ── Impact ──
        if (ctx.HasTag(SpellTag.CHAIN))
            AddBehavior<ChainBehavior>();
        if (!isSplitChild && (ctx.HasTag(SpellTag.FRAGMENTING) || ctx.HasTag(SpellTag.SPLIT_ON_IMPACT)))
            AddBehavior<FragmentingBehavior>();
        if (ctx.HasTag(SpellTag.DETONATING))
            AddBehavior<DetonatingBehavior>();
        if (ctx.HasTag(SpellTag.LINGERING))
            AddBehavior<LingeringBehavior>();
        if (ctx.HasTag(SpellTag.SWAPPING))
            AddBehavior<SwappingBehavior>();
        if (ctx.HasTag(SpellTag.CONTAGIOUS))
            AddBehavior<ContagiousBehavior>();
    }

    private T AddBehavior<T>() where T : ProjectileBehaviorBase
    {
        var b = gameObject.AddComponent<T>();
        b.Initialize(ctx);
        return b;
    }

    // ── Physics ───────────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (!initialized) return;

        // Behaviors that handle their own wall interactions skip this
        if (ctx.HasTag(SpellTag.WALL_BOUNCE)     ||
            ctx.HasTag(SpellTag.REFLECTING)       ||
            ctx.HasTag(SpellTag.PIERCE_WALLS)     ||
            ctx.HasTag(SpellTag.SURFACE_CRAWLING) ||
            ctx.HasTag(SpellTag.BURROWING))
            return;

        // Default: destroy on wall contact
        Vector2      nextPos = ctx.Rb.position + ctx.Rb.linearVelocity * Time.fixedDeltaTime * 2f;
        RaycastHit2D hit     = Physics2D.Linecast(ctx.Rb.position, nextPos, WallLayer);
        if (hit.collider != null) RequestDestroy();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!initialized) return;

        Health h = other.GetComponentInParent<Health>();
        if (h == null || h == ctx.PlayerHealth || h.IsDead) return;
        if (hitEnemies.Contains(h.gameObject)) return;

        HitEnemy(h.gameObject);
    }

    // ── Hit processing ────────────────────────────────────────────────────────

    public void HitEnemy(GameObject enemyObj)
    {
        var h = enemyObj.GetComponent<Health>();
        if (h == null || h.IsDead) return;

        h.TakeDamage(ctx.Spell.damage);
        HitEffectSpawner.SpawnImpactFlash(transform.position,
            ElementToColor(ctx.Spell.element), Color.white);

        SessionLogger.Instance?.RecordDamageDealt(ctx.Spell.element, ctx.Spell.damage);

        if (ctx.HasTag(SpellTag.LIFESTEAL))
            ctx.PlayerHealth?.Heal(ctx.Spell.damage * 0.3f);

        ApplyStatusEffects(enemyObj);
        ApplyForceEffects(enemyObj);

        if (ctx.HasTag(SpellTag.AOE_BURST))
            ApplyAoeBurst(transform.position);

        // Notify behavior components — any may suppress the default destroy
        bool suppressDestroy = false;
        foreach (var behavior in GetComponents<ProjectileBehaviorBase>())
        {
            if (behavior.OnHitEnemy(enemyObj))
                suppressDestroy = true;
        }

        hitEnemies.Add(enemyObj);
        hitCount++;

        if (!suppressDestroy)
        {
            if (!ctx.HasTag(SpellTag.PIERCE) || hitCount >= MaxPierceHits)
                RequestDestroy();
        }
    }

    public void RequestDestroy() => Destroy(gameObject);

    // ── Status effects ────────────────────────────────────────────────────────

    private void ApplyStatusEffects(GameObject enemyObj)
    {
        var status = enemyObj.GetComponent<StatusEffectHandler>()
                  ?? enemyObj.AddComponent<StatusEffectHandler>();

        if (ctx.HasTag(SpellTag.BURN))     status.ApplyBurn(ctx.Spell.damage);
        if (ctx.HasTag(SpellTag.FREEZE))   status.ApplyFreeze();
        if (ctx.HasTag(SpellTag.SLOW))     status.ApplySlow();
        if (ctx.HasTag(SpellTag.STUN))     status.ApplyStun();
        if (ctx.HasTag(SpellTag.POISON))   status.ApplyPoison(ctx.Spell.damage);
        if (ctx.HasTag(SpellTag.BLEED))    status.ApplyBleed(ctx.Spell.damage);
        if (ctx.HasTag(SpellTag.ROOT))     status.ApplyRoot();
        if (ctx.HasTag(SpellTag.WEAKNESS)) status.ApplyWeakness();
        if (ctx.HasTag(SpellTag.CURSE))    status.ApplyCurse();
        if (ctx.HasTag(SpellTag.BLIND))    status.ApplyBlind();
    }

    // ── Force effects ─────────────────────────────────────────────────────────

    private void ApplyForceEffects(GameObject enemyObj)
    {
        if (ctx.HasTag(SpellTag.PUSH))
        {
            var rb = enemyObj.GetComponent<Rigidbody2D>();
            rb?.AddForce(ctx.Rb.linearVelocity.normalized * 8f, ForceMode2D.Impulse);
        }

        if (ctx.HasTag(SpellTag.PULL) && ctx.PlayerHealth != null)
        {
            var rb         = enemyObj.GetComponent<Rigidbody2D>();
            Vector2 toward = ((Vector2)ctx.PlayerHealth.transform.position
                           - (Vector2)enemyObj.transform.position).normalized;
            rb?.AddForce(toward * 10f, ForceMode2D.Impulse);
        }
    }

    private void ApplyAoeBurst(Vector2 center)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, 3f, LayerMask.GetMask("Enemy"));
        foreach (var hit in hits)
        {
            var h = hit.GetComponent<Health>();
            if (h != null && !h.IsDead)
                h.TakeDamage(ctx.Spell.damage * 0.5f);
        }
        if (aoePrefab != null)
            Destroy(Instantiate(aoePrefab, center, Quaternion.identity), 1f);
    }

    // ── Visuals ──────────────────────────────────────────────────────────────

    private void ApplyVisuals(SpellData spell)
    {
        // Primary color: use Gemini's hex color, fall back to element default
        Color primary = !string.IsNullOrEmpty(spell.projectileColor)
            ? ParseHexColor(spell.projectileColor, ElementToColor(spell.element))
            : ElementToColor(spell.element);

        // Secondary color: for trail gradients
        Color secondary = !string.IsNullOrEmpty(spell.secondaryColor)
            ? ParseHexColor(spell.secondaryColor, primary)
            : primary;

        // Scale — Gemini controls projectile size
        float scale = Mathf.Clamp(spell.projectileScale > 0f ? spell.projectileScale : 1f, 0.3f, 4f);
        transform.localScale = Vector3.one * scale;

        // Glow — Gemini controls glow radius
        float glowSize = Mathf.Clamp(spell.glowSize > 0f ? spell.glowSize : 0.35f, 0.1f, 2f);
        HitEffectSpawner.AddGlowSprite(transform, primary, glowSize, 5);

        // Trail — only added if trail_length > 0
        float trailLen = spell.trailLength;
        if (trailLen > 0.01f)
        {
            float trailW = Mathf.Clamp(spell.trailWidth > 0f ? spell.trailWidth : 0.15f, 0.02f, 0.8f);
            HitEffectSpawner.AddTrailRenderer(gameObject, primary, secondary,
                Mathf.Clamp(trailLen, 0.02f, 0.8f), trailW * scale);
        }
    }

    private static Color ParseHexColor(string hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        if (hex[0] != '#') hex = "#" + hex;
        return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Exposed so behavior components (e.g. DetonatingBehavior) can call SpawnImpactFlash.</summary>
    public static Color ElementToColor(string element) => element?.ToLower() switch
    {
        "fire"      => new Color(1.0f, 0.4f, 0.1f),
        "ice"       => new Color(0.5f, 0.85f, 1.0f),
        "poison"    => new Color(0.3f, 0.9f, 0.2f),
        "lightning" => new Color(0.9f, 0.85f, 0.2f),
        "void"      => new Color(0.6f, 0.2f, 1.0f),
        "shadow"    => new Color(0.4f, 0.1f, 0.6f),
        _           => new Color(0.6f, 0.8f, 1.0f),
    };
}
