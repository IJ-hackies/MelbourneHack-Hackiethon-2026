using System.Collections;
using UnityEngine;

/// <summary>
/// DETONATING — embeds in the first enemy hit; explodes for 2× AoE damage after a 2.5s fuse.
/// Pulses in scale while armed.
/// </summary>
public class DetonatingBehavior : ProjectileBehaviorBase
{
    private bool armed;
    private const float FuseTime       = 2.5f;
    private const float ExplosionRadius = 3.5f;

    public override bool OnHitEnemy(GameObject enemy)
    {
        if (armed) return true;
        armed = true;

        ctx.Rb.linearVelocity = Vector2.zero;
        ctx.Rb.bodyType       = RigidbodyType2D.Kinematic;
        transform.SetParent(enemy.transform, true);

        StartCoroutine(FuseRoutine());
        return true; // suppress destroy — we detonate manually
    }

    private IEnumerator FuseRoutine()
    {
        float   elapsed   = 0f;
        Vector3 baseScale = transform.localScale;

        while (elapsed < FuseTime)
        {
            elapsed += Time.deltaTime;
            // Rapid pulse gets faster as fuse nears zero
            float pulseSpeed = Mathf.Lerp(4f, 16f, elapsed / FuseTime);
            float scale      = 1f + Mathf.Abs(Mathf.Sin(elapsed * Mathf.PI * pulseSpeed)) * 0.3f;
            transform.localScale = baseScale * scale;
            yield return null;
        }

        // Explode
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, ExplosionRadius, ProjectileHandler.EnemyMask);
        foreach (var hit in hits)
        {
            var h = hit.GetComponentInParent<Health>();
            if (h != null && !h.IsDead)
                h.TakeDamage(ctx.Spell.damage * 2f);
        }

        HitEffectSpawner.SpawnImpactFlash(transform.position, ProjectileHandler.ElementToColor(ctx.Spell.element), Color.white);
        ctx.Handler.RequestDestroy();
    }
}
