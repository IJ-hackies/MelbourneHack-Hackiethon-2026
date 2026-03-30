using System.Collections;
using UnityEngine;

/// <summary>
/// Reads the active spell's tags and dispatches the correct cast behavior.
/// Handles all caster-level concerns: MIRRORED, GHOST_CAST, SACRIFICE, ECHOING.
/// Per-projectile behavior is delegated to ProjectileHandler and its behavior components.
/// </summary>
public class SpellExecutor : MonoBehaviour
{
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;

    [Header("Orbital")]
    [SerializeField] private GameObject orbitalPrefab;

#pragma warning disable CS0414
    private float    lastCastTime = -999f;
#pragma warning restore CS0414
    private Health   playerHealth;
    private Coroutine echoRoutine;

    private void Awake()
    {
        playerHealth = GetComponent<Health>();
    }

    private void Update()
    {
        if (IsAttackHeld())
            TryCast();
    }

    private static bool IsAttackHeld()
    {
        KeyCode key = SettingsData.Attack;
        // Input.GetMouseButton is more reliable for held mouse buttons
        if (key == KeyCode.Mouse0) return Input.GetMouseButton(0);
        if (key == KeyCode.Mouse1) return Input.GetMouseButton(1);
        if (key == KeyCode.Mouse2) return Input.GetMouseButton(2);
        return Input.GetKey(key);
    }

    private void TryCast()
    {
        Grimoire grimoire = Grimoire.Instance;
        if (grimoire == null) return;

        SpellData spell = grimoire.ActiveSpell;
        if (spell == null) return;

        int slot = grimoire.ActiveSlot;
        if (!grimoire.IsSlotReady(slot)) return;

        grimoire.RecordCast(slot);
        ExecuteSpell(spell);
    }

    private void ExecuteSpell(SpellData spell)
    {
        Vector2 aimDir = GetAimDirection();
        if (spell.HasTag(SpellTag.REVERSED_CONTROLS)) aimDir = -aimDir;

        // SACRIFICE — 15% HP cost, 2× damage multiplier
        float dmgMult = 1f;
        if (spell.HasTag(SpellTag.SACRIFICE) && playerHealth != null)
        {
            float cost = playerHealth.Current * 0.15f;
            playerHealth.TakeDamage(cost);
            dmgMult = 2f;
        }

        if (spell.isMerged && spell.mergedSourceSpells != null && spell.mergedSourceSpells.Length > 0)
        {
            // Fire each source spell's projectile simultaneously with reduced damage
            float perSpellMult = dmgMult * 0.5f;
            foreach (var src in spell.mergedSourceSpells)
            {
                if (src == null) continue;
                if (src.HasTag(SpellTag.BEAM))      HandleBeam(src, aimDir, perSpellMult);
                if (src.HasTag(SpellTag.ORBITAL))    HandleOrbital(src);
                if (src.HasTag(SpellTag.PROJECTILE) || src.HasTag(SpellTag.CHANNELED))
                {
                    HandleProjectile(src, aimDir, damageMultiplier: perSpellMult);
                    if (src.HasTag(SpellTag.DOUBLE_HIT))
                        HandleProjectile(src, Quaternion.Euler(0, 0, 15f) * (Vector3)aimDir, damageMultiplier: perSpellMult);
                }
            }
        }
        else
        {
            if      (spell.HasTag(SpellTag.BEAM))    HandleBeam(spell, aimDir);
            else if (spell.HasTag(SpellTag.ORBITAL)) HandleOrbital(spell);
            else if (spell.HasTag(SpellTag.PROJECTILE) || spell.HasTag(SpellTag.CHANNELED))
            {
                HandleProjectile(spell, aimDir, damageMultiplier: dmgMult);
                if (spell.HasTag(SpellTag.DOUBLE_HIT))
                    HandleProjectile(spell, Quaternion.Euler(0, 0, 15f) * (Vector3)aimDir, damageMultiplier: dmgMult);
            }
        }

        // MIRRORED — identical copy fired in the opposite direction
        if (spell.HasTag(SpellTag.MIRRORED))
        {
            if (spell.isMerged && spell.mergedSourceSpells != null)
            {
                foreach (var src in spell.mergedSourceSpells)
                    if (src != null && (src.HasTag(SpellTag.PROJECTILE) || src.HasTag(SpellTag.CHANNELED)))
                        HandleProjectile(src, -aimDir, damageMultiplier: dmgMult * 0.5f);
            }
            else
                HandleProjectile(spell, -aimDir, damageMultiplier: dmgMult);
        }

        // GHOST_CAST — invisible damage copy alongside the visible spell
        if (spell.HasTag(SpellTag.GHOST_CAST))
        {
            if (spell.isMerged && spell.mergedSourceSpells != null)
            {
                foreach (var src in spell.mergedSourceSpells)
                    if (src != null && (src.HasTag(SpellTag.PROJECTILE) || src.HasTag(SpellTag.CHANNELED)))
                        HandleProjectile(src, aimDir, isGhost: true, damageMultiplier: dmgMult * 0.5f);
            }
            else
                HandleProjectile(spell, aimDir, isGhost: true, damageMultiplier: dmgMult);
        }

        // ECHOING — re-casts automatically after 3s
        if (spell.HasTag(SpellTag.ECHOING))
        {
            if (echoRoutine != null) StopCoroutine(echoRoutine);
            echoRoutine = StartCoroutine(EchoCast(spell));
        }

        // SELF_DAMAGE corruption
        if (spell.HasTag(SpellTag.SELF_DAMAGE))
            playerHealth?.TakeDamage(spell.damage * 0.2f);

        Grimoire.Instance?.RecordSpellUsed(spell);
    }

    // ── Echo ─────────────────────────────────────────────────────────────────

    private IEnumerator EchoCast(SpellData spell)
    {
        yield return new WaitForSeconds(3f);
        // Re-cast silently (no cooldown check, no echo chain)
        Vector2 aimDir = GetAimDirection();
        HandleProjectile(spell, aimDir);
    }

    // ── Movement handlers ─────────────────────────────────────────────────────

    private void HandleProjectile(SpellData spell, Vector2 dir,
                                  bool isGhost = false, float damageMultiplier = 1f)
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning("[SpellExecutor] No projectilePrefab assigned.");
            return;
        }

        int count = Mathf.Clamp(spell.burstCount, 1, 5);
        float spreadAngle = count > 1 ? 30f : 0f; // total spread across all projectiles
        float step = count > 1 ? spreadAngle / (count - 1) : 0f;
        float startAngle = -spreadAngle / 2f;

        for (int i = 0; i < count; i++)
        {
            float angle = startAngle + step * i;
            Vector2 shotDir = count > 1
                ? (Vector2)(Quaternion.Euler(0, 0, angle) * (Vector3)dir)
                : dir;

            var proj    = Instantiate(projectilePrefab, transform.position, Quaternion.identity);
            var handler = proj.GetComponent<ProjectileHandler>();
            if (handler != null)
            {
                handler.sourcePrefab = projectilePrefab;
                handler.Init(spell, shotDir, isGhost: isGhost, damageMultiplier: damageMultiplier);
            }
        }
    }

    private const int MaxOrbitals = 3;

    private void HandleOrbital(SpellData spell)
    {
        int existing = 0;
        foreach (Transform child in transform)
            if (child.GetComponent<OrbitalMotion>() != null) existing++;

        if (existing >= MaxOrbitals) return;

        GameObject orb;
        if (orbitalPrefab != null)
        {
            orb = Instantiate(orbitalPrefab, transform.position, Quaternion.identity, transform);
        }
        else
        {
            // Create orbital dynamically when no prefab is assigned
            orb = new GameObject("Orbital_" + spell.spellName);
            orb.transform.SetParent(transform, false);
            int projLayer = projectilePrefab != null
                ? projectilePrefab.layer
                : LayerMask.NameToLayer("PlayerProjectile");
            if (projLayer >= 0) orb.layer = projLayer;

            var sr = orb.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCircleSprite();
            sr.sortingOrder = 5;

            var rb = orb.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var col = orb.AddComponent<CircleCollider2D>();
            col.radius = 0.3f;
            col.isTrigger = true;
        }

        var orbital = orb.GetComponent<OrbitalMotion>() ?? orb.AddComponent<OrbitalMotion>();
        orbital.Init(spell, existing);
    }

    private void HandleBeam(SpellData spell, Vector2 dir, float damageMultiplier = 1f)
    {
        const float BeamRange = 15f;
        int wallMask = 1 << 9; // Wall layer

        // Find where the beam ends (wall hit or max range)
        Vector2 origin = transform.position;
        RaycastHit2D wallHit = Physics2D.Raycast(origin, dir, BeamRange, wallMask);
        float beamDist = wallHit.collider != null ? wallHit.distance : BeamRange;
        Vector2 beamEnd = origin + dir * beamDist;

        float dmg = spell.damage * damageMultiplier;

        // Deal damage to enemies along the beam
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, beamDist, ProjectileHandler.EnemyMask);

        foreach (var hit in hits)
        {
            var h = hit.collider.GetComponent<Health>();
            if (h == null || h.IsDead) continue;

            h.TakeDamage(dmg);
            SessionLogger.Instance?.RecordDamageDealt(spell.element, dmg);

            if (spell.HasTag(SpellTag.LIFESTEAL))
                playerHealth?.Heal(dmg * 0.3f);

            ApplyBeamStatusEffects(spell, hit.collider.gameObject, hit.point);

            if (!spell.HasTag(SpellTag.PIERCE)) break;
        }

        // Spawn a visible beam line
        SpawnBeamVisual(origin, beamEnd, spell);
    }

    private void ApplyBeamStatusEffects(SpellData spell, GameObject enemyObj, Vector2 hitPoint)
    {
        var status = enemyObj.GetComponent<StatusEffectHandler>()
                  ?? enemyObj.AddComponent<StatusEffectHandler>();

        if (spell.HasTag(SpellTag.BURN))     status.ApplyBurn(spell.damage);
        if (spell.HasTag(SpellTag.FREEZE))   status.ApplyFreeze();
        if (spell.HasTag(SpellTag.SLOW))     status.ApplySlow();
        if (spell.HasTag(SpellTag.STUN))     status.ApplyStun();
        if (spell.HasTag(SpellTag.POISON))   status.ApplyPoison(spell.damage);
        if (spell.HasTag(SpellTag.BLEED))    status.ApplyBleed(spell.damage);
        if (spell.HasTag(SpellTag.ROOT))     status.ApplyRoot();
        if (spell.HasTag(SpellTag.WEAKNESS)) status.ApplyWeakness();
        if (spell.HasTag(SpellTag.CURSE))    status.ApplyCurse();
        if (spell.HasTag(SpellTag.BLIND))    status.ApplyBlind();

        if (spell.HasTag(SpellTag.PUSH))
        {
            var rb        = enemyObj.GetComponent<Rigidbody2D>();
            Vector2 away  = ((Vector2)enemyObj.transform.position - (Vector2)transform.position).normalized;
            rb?.AddForce(away * 8f, ForceMode2D.Impulse);
        }

        if (spell.HasTag(SpellTag.PULL))
        {
            var rb        = enemyObj.GetComponent<Rigidbody2D>();
            Vector2 toward = ((Vector2)transform.position - (Vector2)enemyObj.transform.position).normalized;
            rb?.AddForce(toward * 10f, ForceMode2D.Impulse);
        }

        if (spell.HasTag(SpellTag.AOE_BURST))
        {
            Collider2D[] aoeHits = Physics2D.OverlapCircleAll(hitPoint, 3f, ProjectileHandler.EnemyMask);
            foreach (var aoe in aoeHits)
            {
                var aoeH = aoe.GetComponent<Health>();
                if (aoeH != null && !aoeH.IsDead && aoe.gameObject != enemyObj)
                    aoeH.TakeDamage(spell.damage * 0.5f);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Vector2 GetAimDirection()
    {
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        return ((Vector2)(mouseWorld - transform.position)).normalized;
    }

    private void SpawnBeamVisual(Vector2 start, Vector2 end, SpellData spell)
    {
        var beamObj = new GameObject("BeamVisual");
        var lr = beamObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);

        Color beamColor = ProjectileHandler.GetSpellColor(spell);

        lr.startWidth = 0.25f;
        lr.endWidth = 0.15f;
        lr.startColor = beamColor;
        lr.endColor = beamColor;

        // Use same URP-safe material pattern as HitEffectSpawner
        lr.material = HitEffectSpawner.GetAdditiveParticleMaterial();
        lr.sortingLayerName = "Entities";
        lr.sortingOrder = 200;

        // Fade out over 0.15s
        StartCoroutine(FadeBeam(lr, beamColor, 0.15f));
    }

    private IEnumerator FadeBeam(LineRenderer lr, Color color, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, t / duration);
            Color c = new Color(color.r, color.g, color.b, a);
            lr.startColor = c;
            lr.endColor = c;
            lr.startWidth = Mathf.Lerp(0.25f, 0.05f, t / duration);
            lr.endWidth = Mathf.Lerp(0.15f, 0.02f, t / duration);
            yield return null;
        }
        Destroy(lr.gameObject);
    }

    private static Sprite circleSprite;
    private static Sprite CreateCircleSprite()
    {
        if (circleSprite != null) return circleSprite;
        int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        float center = (size - 1) / 2f;
        float radiusSq = center * center;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center, dy = y - center;
                tex.SetPixel(x, y, dx * dx + dy * dy <= radiusSq
                    ? Color.white : Color.clear);
            }
        tex.Apply();
        circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f), size);
        return circleSprite;
    }
}
