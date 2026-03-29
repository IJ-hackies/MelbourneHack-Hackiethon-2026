using System.Collections;
using UnityEngine;

/// <summary>
/// World-space damage zone left by a LINGERING spell.
/// Spawned at impact by LingeringBehavior; lives independently for a fixed duration.
/// </summary>
public class LingeringZone : MonoBehaviour
{
    private float tickDamage;
    private float duration;
    private float radius;

    /// <param name="damagePerTick">Damage applied to each enemy every 0.5s.</param>
    /// <param name="duration">Total seconds the zone lives.</param>
    /// <param name="radius">Overlap radius in world units.</param>
    public void Init(float damagePerTick, float duration, float radius)
    {
        this.tickDamage = damagePerTick;
        this.duration   = duration;
        this.radius     = radius;
        StartCoroutine(ZoneRoutine());
    }

    private IEnumerator ZoneRoutine()
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;

            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius, ProjectileHandler.EnemyMask);
            foreach (var hit in hits)
            {
                var h = hit.GetComponentInParent<Health>();
                if (h != null && !h.IsDead)
                    h.TakeDamage(tickDamage);
            }
        }

        Destroy(gameObject);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.5f, 0f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
