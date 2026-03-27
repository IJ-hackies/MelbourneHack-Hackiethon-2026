using UnityEngine;

/// <summary>
/// Reads the active spell's tags and composes the cast behavior.
/// Attach to the Player. Extend each Handle* method as visuals/prefabs are added.
/// </summary>
public class SpellExecutor : MonoBehaviour
{
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;

    private float lastCastTime = -999f;

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
            TryCast();
    }

    private void TryCast()
    {
        SpellData spell = Grimoire.Instance?.ActiveSpell;
        if (spell == null) return;
        if (Time.time - lastCastTime < spell.cooldown) return;

        lastCastTime = Time.time;
        ExecuteSpell(spell);
    }

    private void ExecuteSpell(SpellData spell)
    {
        // Corruption: reversed controls inverts the aim direction
        Vector2 aimDir = GetAimDirection();
        if (spell.HasTag(SpellTag.REVERSED_CONTROLS))
            aimDir = -aimDir;

        // Movement tags — pick the dominant one (order = priority)
        if (spell.HasTag(SpellTag.BEAM))
            HandleBeam(spell, aimDir);
        else if (spell.HasTag(SpellTag.ORBITAL))
            HandleOrbital(spell);
        else if (spell.HasTag(SpellTag.PROJECTILE))
            HandleProjectile(spell, aimDir);

        // Post-cast corruption
        if (spell.HasTag(SpellTag.SELF_DAMAGE))
            HandleSelfDamage(spell);
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
        // TODO: spawn an orbiting projectile around the player
        Debug.Log($"[SpellExecutor] ORBITAL cast: {spell.spellName}");
    }

    private void HandleBeam(SpellData spell, Vector2 dir)
    {
        // TODO: fire a beam in aim direction
        Debug.Log($"[SpellExecutor] BEAM cast: {spell.spellName}");
    }

    // --- Corruption handlers ---

    private void HandleSelfDamage(SpellData spell)
    {
        // TODO: apply self-damage via player health component
        Debug.Log($"[SpellExecutor] SELF_DAMAGE triggered by {spell.spellName}");
    }

    // --- Helpers ---

    private Vector2 GetAimDirection()
    {
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        return ((Vector2)(mouseWorld - transform.position)).normalized;
    }
}
