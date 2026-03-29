using System.Collections.Generic;
using UnityEngine;

/// <summary>CHAIN — after each hit, redirects toward the nearest un-hit enemy within range.</summary>
public class ChainBehavior : ProjectileBehaviorBase
{
    private readonly HashSet<GameObject> chained = new();
    private const float ChainRange = 6f;

    public override bool OnHitEnemy(GameObject enemy)
    {
        chained.Add(enemy);
        ChainToNext();
        return true; // suppress default destroy; we'll destroy when no target is found
    }

    private void ChainToNext()
    {
        EnemyBase[] all  = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);
        EnemyBase   next = null;
        float nearest    = float.MaxValue;

        foreach (var e in all)
        {
            if (e.IsDead || chained.Contains(e.gameObject)) continue;
            float d = Vector2.Distance(ctx.Rb.position, e.transform.position);
            if (d < nearest && d <= ChainRange) { nearest = d; next = e; }
        }

        if (next == null) { ctx.Handler.RequestDestroy(); return; }

        ctx.Rb.linearVelocity = ((Vector2)next.transform.position - ctx.Rb.position).normalized * ctx.Spell.speed;
    }
}
