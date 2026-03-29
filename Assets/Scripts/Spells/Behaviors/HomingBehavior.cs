using UnityEngine;

/// <summary>HOMING / ENEMY_HOMING — curves toward nearest enemy (or player if ENEMY_HOMING).</summary>
public class HomingBehavior : ProjectileBehaviorBase
{
    private const float TurnDegreesPerSecond = 180f;

    private void FixedUpdate()
    {
        if (ctx == null) return;

        Transform target = ctx.HasTag(SpellTag.ENEMY_HOMING)
            ? (ctx.PlayerHealth != null ? ctx.PlayerHealth.transform : null)
            : FindNearestEnemy();

        if (target == null) return;

        Vector2 toTarget = ((Vector2)target.position - ctx.Rb.position).normalized;
        float turnRad    = TurnDegreesPerSecond * Mathf.Deg2Rad * Time.fixedDeltaTime;
        Vector2 newDir   = Vector2.MoveTowards(ctx.Rb.linearVelocity.normalized, toTarget, turnRad);
        ctx.Rb.linearVelocity = newDir.normalized * ctx.Spell.speed;
    }

    private Transform FindNearestEnemy()
    {
        EnemyBase[] enemies = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);
        Transform nearest   = null;
        float minDist       = float.MaxValue;
        foreach (var e in enemies)
        {
            if (e.IsDead) continue;
            float d = Vector2.Distance(ctx.Rb.position, e.transform.position);
            if (d < minDist) { minDist = d; nearest = e.transform; }
        }
        return nearest;
    }
}
