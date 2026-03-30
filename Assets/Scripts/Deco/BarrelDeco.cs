using System.Collections;
using UnityEngine;

/// <summary>
/// Barrel decoration — plays a particle burst when hit by a spell projectile.
/// Never breaks. Short per-barrel cooldown prevents effect spam.
/// Requires a trigger Collider2D on this GameObject.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BarrelDeco : MonoBehaviour
{
    [SerializeField] private Color particleColor      = new Color(0.55f, 0.40f, 0.25f);
    [SerializeField] private Color particleColorAlt   = new Color(0.80f, 0.65f, 0.40f);
    [SerializeField] private float hitCooldown        = 0.5f;

    private float cooldownTimer;

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;

        if (GetComponent<Rigidbody2D>() == null)
        {
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType    = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
        }
    }

    private void Update()
    {
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (cooldownTimer > 0f) return;
        if (other.GetComponent<ProjectileHandler>() == null) return;

        HitEffectSpawner.SpawnHit(transform.position, particleColor, particleColorAlt);
        cooldownTimer = hitCooldown;
    }
}
