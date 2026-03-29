using UnityEngine;

/// <summary>
/// Reads the active spell's tags and composes the cast behavior.
/// Attach to the Player. Extend each Handle* method as visuals/prefabs are added.
/// </summary>
public class SpellExecutor : MonoBehaviour
{
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;

    [Header("Orbital")]
    [SerializeField] private GameObject orbitalPrefab;

    private Health playerHealth;

    private void Awake()
    {
        playerHealth = GetComponent<Health>();
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
            TryCast();
    }

    private void TryCast()
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null) return;

        int slot = grimoire.ActiveSlot;
        SpellData spell = grimoire.ActiveSpell;
        if (spell == null) return;
        if (!grimoire.IsSlotReady(slot)) return;

        grimoire.RecordCast(slot);
        ExecuteSpell(spell);
    }

    private void ExecuteSpell(SpellData spell)
    {
        Vector2 aimDir = GetAimDirection();
        if (spell.HasTag(SpellTag.REVERSED_CONTROLS))
            aimDir = -aimDir;

        if (spell.isMerged)
        {
            // Merged spells fire ALL movement types present in their tag list simultaneously
            if (spell.HasTag(SpellTag.BEAM))    HandleBeam(spell, aimDir);
            if (spell.HasTag(SpellTag.ORBITAL)) HandleOrbital(spell);
            if (spell.HasTag(SpellTag.PROJECTILE))
            {
                HandleProjectile(spell, aimDir);
                if (spell.HasTag(SpellTag.DOUBLE_HIT))
                    HandleProjectile(spell, Quaternion.Euler(0, 0, 15f) * (Vector3)aimDir);
            }
        }
        else
        {
            // Normal spells: exactly one movement type fires (BEAM > ORBITAL > PROJECTILE)
            if (spell.HasTag(SpellTag.BEAM))
                HandleBeam(spell, aimDir);
            else if (spell.HasTag(SpellTag.ORBITAL))
                HandleOrbital(spell);
            else if (spell.HasTag(SpellTag.PROJECTILE))
            {
                HandleProjectile(spell, aimDir);
                if (spell.HasTag(SpellTag.DOUBLE_HIT))
                    HandleProjectile(spell, Quaternion.Euler(0, 0, 15f) * (Vector3)aimDir);
            }
        }

        if (spell.HasTag(SpellTag.SELF_DAMAGE))
            HandleSelfDamage(spell);

        Grimoire.Instance?.RecordSpellUsed(spell);
    }

    // --- Movement handlers ---

    private void HandleProjectile(SpellData spell, Vector2 dir)
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning("SpellExecutor: no projectilePrefab assigned.");
            return;
        }

        GameObject proj = Instantiate(projectilePrefab, transform.position, Quaternion.identity);
        var handler = proj.GetComponent<ProjectileHandler>();
        if (handler != null)
            handler.Init(spell, dir);
    }

    private void HandleOrbital(SpellData spell)
    {
        if (orbitalPrefab == null)
        {
            Debug.LogWarning("SpellExecutor: no orbitalPrefab assigned.");
            return;
        }

        // Space multiple orbitals evenly (up to 3)
        int existing = 0;
        foreach (Transform child in transform)
            if (child.GetComponent<OrbitalMotion>() != null)
                existing++;

        GameObject orb = Instantiate(orbitalPrefab, transform.position, Quaternion.identity, transform);
        var orbital = orb.GetComponent<OrbitalMotion>();
        if (orbital == null) orbital = orb.AddComponent<OrbitalMotion>();
        orbital.Init(spell, existing);
    }

    private void HandleBeam(SpellData spell, Vector2 dir)
    {
        float beamRange = 15f;
        RaycastHit2D[] hits = Physics2D.RaycastAll(transform.position, dir, beamRange, LayerMask.GetMask("Enemy"));

        foreach (var hit in hits)
        {
            var h = hit.collider.GetComponent<Health>();
            if (h == null || h.IsDead) continue;

            h.TakeDamage(spell.damage);

            if (spell.HasTag(SpellTag.LIFESTEAL))
                playerHealth?.Heal(spell.damage * 0.3f);

            ApplyHitEffects(spell, hit.collider.gameObject, hit.point);

            if (!spell.HasTag(SpellTag.PIERCE))
                break;
        }

        // TODO: add LineRenderer visual
        Debug.Log($"[SpellExecutor] BEAM cast: {spell.spellName}");
    }

    // --- Shared per-hit effect application (used by beam; projectile uses ProjectileHandler) ---

    /// <summary>Applies status effects, PUSH, PULL, and AOE_BURST to a hit enemy.</summary>
    private void ApplyHitEffects(SpellData spell, GameObject enemyObj, Vector2 hitPoint)
    {
        var status = enemyObj.GetComponent<StatusEffectHandler>()
                     ?? enemyObj.AddComponent<StatusEffectHandler>();

        if (spell.HasTag(SpellTag.BURN))   status.ApplyBurn(spell.damage);
        if (spell.HasTag(SpellTag.FREEZE)) status.ApplyFreeze();
        if (spell.HasTag(SpellTag.SLOW))   status.ApplySlow();
        if (spell.HasTag(SpellTag.STUN))   status.ApplyStun();
        if (spell.HasTag(SpellTag.POISON)) status.ApplyPoison(spell.damage);

        if (spell.HasTag(SpellTag.PUSH))
        {
            var enemyRb = enemyObj.GetComponent<Rigidbody2D>();
            Vector2 pushDir = ((Vector2)enemyObj.transform.position - (Vector2)transform.position).normalized;
            enemyRb?.AddForce(pushDir * 8f, ForceMode2D.Impulse);
        }

        if (spell.HasTag(SpellTag.PULL))
        {
            var enemyRb = enemyObj.GetComponent<Rigidbody2D>();
            Vector2 pullDir = ((Vector2)transform.position - (Vector2)enemyObj.transform.position).normalized;
            enemyRb?.AddForce(pullDir * 10f, ForceMode2D.Impulse);
        }

        if (spell.HasTag(SpellTag.AOE_BURST))
        {
            Collider2D[] aoeHits = Physics2D.OverlapCircleAll(hitPoint, 3f, LayerMask.GetMask("Enemy"));
            foreach (var aoeHit in aoeHits)
            {
                var aooh = aoeHit.GetComponent<Health>();
                if (aooh != null && !aooh.IsDead && aoeHit.gameObject != enemyObj)
                    aooh.TakeDamage(spell.damage * 0.5f);
            }
        }
    }

    // --- Corruption handlers ---

    private void HandleSelfDamage(SpellData spell)
    {
        playerHealth?.TakeDamage(spell.damage * 0.2f);
    }

    // --- Helpers ---

    private Vector2 GetAimDirection()
    {
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        return ((Vector2)(mouseWorld - transform.position)).normalized;
    }
}
