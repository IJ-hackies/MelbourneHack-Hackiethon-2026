using UnityEngine;

/// <summary>
/// FRAGMENTING / SPLIT_ON_IMPACT — on impact spawns 4–6 projectiles in chaotic random directions.
/// Fragments are marked as split-children so they don't fragment again.
/// </summary>
public class FragmentingBehavior : ProjectileBehaviorBase
{
    private const int   MinFragments = 4;
    private const int   MaxFragments = 6;

    public override bool OnHitEnemy(GameObject enemy)
    {
        SpawnFragments();
        ctx.Handler.RequestDestroy();
        return true;
    }

    private void SpawnFragments()
    {
        GameObject prefab = ctx.ProjectilePrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[FragmentingBehavior] No projectile prefab — cannot spawn fragments.");
            return;
        }

        int   count     = Random.Range(MinFragments, MaxFragments + 1);
        float baseAngle = Mathf.Atan2(ctx.Rb.linearVelocity.y, ctx.Rb.linearVelocity.x) * Mathf.Rad2Deg;

        for (int i = 0; i < count; i++)
        {
            float   angle = (baseAngle + Random.Range(-160f, 160f)) * Mathf.Deg2Rad;
            Vector2 dir   = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var child     = Instantiate(prefab, transform.position, Quaternion.identity);
            var handler   = child.GetComponent<ProjectileHandler>();
            if (handler != null)
            {
                handler.sourcePrefab = prefab;
                handler.Init(ctx.Spell, dir, isSplitChild: true);
            }
        }
    }
}
