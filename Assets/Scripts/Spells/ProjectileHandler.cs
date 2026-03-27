using UnityEngine;

/// <summary>
/// Attached to a projectile prefab. Initialized by SpellExecutor with the
/// casting spell's data and aim direction. Reads tags to modify behavior.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class ProjectileHandler : MonoBehaviour
{
    private SpellData spell;
    private Vector2 direction;
    private Rigidbody2D rb;

    private float lifetime = 5f;
    private bool initialized = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
    }

    public void Init(SpellData spellData, Vector2 dir)
    {
        spell = spellData;
        direction = dir;
        initialized = true;

        rb.linearVelocity = direction * spell.speed;

        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        if (!initialized) return;

        if (spell.HasTag(SpellTag.HOMING))
            ApplyHoming();

        if (spell.HasTag(SpellTag.SPIRAL))
            ApplySpiral();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!initialized) return;

        // TODO: check enemy layer, apply damage via enemy health component
        // spell.HasTag(SpellTag.PIERCE) → don't destroy on first hit
        // spell.HasTag(SpellTag.AOE_BURST) → spawn AOE at impact point
        // spell.HasTag(SpellTag.CHAIN) → seek next nearby enemy
        // spell.HasTag(SpellTag.SPLIT_ON_IMPACT) → spawn child projectiles
        // spell.HasTag(SpellTag.WALL_BOUNCE) → reflect off walls

        bool pierce = spell.HasTag(SpellTag.PIERCE);
        if (!pierce)
            Destroy(gameObject);
    }

    // --- Tag behavior stubs ---

    private void ApplyHoming()
    {
        // TODO: find nearest enemy transform, steer toward it
    }

    private void ApplySpiral()
    {
        // TODO: rotate velocity vector by a small angle each FixedUpdate
    }
}
