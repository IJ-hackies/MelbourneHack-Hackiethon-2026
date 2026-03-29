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
        if (Input.GetKeyDown(SettingsData.Attack))
            TryCast();
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

        if (spell.isMerged)
        {
            if (spell.HasTag(SpellTag.BEAM))                                     HandleBeam(spell, aimDir);
            if (spell.HasTag(SpellTag.ORBITAL))                                  HandleOrbital(spell);
            if (spell.HasTag(SpellTag.PROJECTILE) || spell.HasTag(SpellTag.CHANNELED))
            {
                HandleProjectile(spell, aimDir, damageMultiplier: dmgMult);
                if (spell.HasTag(SpellTag.DOUBLE_HIT))
                    HandleProjectile(spell, Quaternion.Euler(0, 0, 15f) * (Vector3)aimDir, damageMultiplier: dmgMult);
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
            HandleProjectile(spell, -aimDir, damageMultiplier: dmgMult);

        // GHOST_CAST — invisible damage copy alongside the visible spell
        if (spell.HasTag(SpellTag.GHOST_CAST))
            HandleProjectile(spell, aimDir, isGhost: true, damageMultiplier: dmgMult);

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
                handler.Init(spell, shotDir, isGhost: isGhost, damageMultiplier: damageMultiplier);
        }
    }

    private void HandleOrbital(SpellData spell)
    {
        if (orbitalPrefab == null)
        {
            Debug.LogWarning("[SpellExecutor] No orbitalPrefab assigned.");
            return;
        }

        int existing = 0;
        foreach (Transform child in transform)
            if (child.GetComponent<OrbitalMotion>() != null) existing++;

        var orb     = Instantiate(orbitalPrefab, transform.position, Quaternion.identity, transform);
        var orbital = orb.GetComponent<OrbitalMotion>() ?? orb.AddComponent<OrbitalMotion>();
        orbital.Init(spell, existing);
    }

    private void HandleBeam(SpellData spell, Vector2 dir)
    {
        const float BeamRange = 15f;
        RaycastHit2D[] hits = Physics2D.RaycastAll(transform.position, dir, BeamRange, ProjectileHandler.EnemyMask);

        foreach (var hit in hits)
        {
            var h = hit.collider.GetComponent<Health>();
            if (h == null || h.IsDead) continue;

            h.TakeDamage(spell.damage);
            SessionLogger.Instance?.RecordDamageDealt(spell.element, spell.damage);

            if (spell.HasTag(SpellTag.LIFESTEAL))
                playerHealth?.Heal(spell.damage * 0.3f);

            ApplyBeamStatusEffects(spell, hit.collider.gameObject, hit.point);

            if (!spell.HasTag(SpellTag.PIERCE)) break;
        }
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
}
