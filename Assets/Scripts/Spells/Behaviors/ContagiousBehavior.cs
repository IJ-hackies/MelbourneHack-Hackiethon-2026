using UnityEngine;

/// <summary>
/// CONTAGIOUS — on hit, spawns a 40%-damage copy of the spell from the hit enemy
/// aimed at the next nearest enemy. The copy cannot be contagious itself.
/// </summary>
public class ContagiousBehavior : ProjectileBehaviorBase
{
    private const float DamageFraction = 0.4f;

    public override bool OnHitEnemy(GameObject enemy)
    {
        SpawnCopy(enemy.transform.position, enemy);
        return false;
    }

    private void SpawnCopy(Vector2 origin, GameObject excludeEnemy)
    {
        // Find nearest enemy that isn't the one we just hit
        EnemyBase[] all  = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);
        EnemyBase   next = null;
        float minDist    = float.MaxValue;

        foreach (var e in all)
        {
            if (e.IsDead || e.gameObject == excludeEnemy) continue;
            float d = Vector2.Distance(origin, e.transform.position);
            if (d < minDist) { minDist = d; next = e; }
        }

        if (next == null) return;

        // Build a weakened spell copy (no CONTAGIOUS tag — stops infinite spread)
        SpellData weak   = ScriptableObject.CreateInstance<SpellData>();
        weak.spellName   = ctx.Spell.spellName;
        weak.flavor      = ctx.Spell.flavor;
        weak.tags        = RemoveContagious(ctx.Spell.tags);
        weak.damage      = ctx.Spell.damage * DamageFraction;
        weak.speed       = ctx.Spell.speed;
        weak.cooldown    = ctx.Spell.cooldown;
        weak.element     = ctx.Spell.element;

        Vector2 dir = ((Vector2)next.transform.position - origin).normalized;
        GameObject prefab = ctx.ProjectilePrefab;
        if (prefab == null) return;

        var child   = Instantiate(prefab, origin, Quaternion.identity);
        var handler = child.GetComponent<ProjectileHandler>();
        if (handler != null)
        {
            handler.sourcePrefab = prefab;
            handler.Init(weak, dir, isSplitChild: true);
        }
    }

    private static SpellTag[] RemoveContagious(SpellTag[] tags)
    {
        if (tags == null) return System.Array.Empty<SpellTag>();
        var list = new System.Collections.Generic.List<SpellTag>(tags);
        list.Remove(SpellTag.CONTAGIOUS);
        return list.ToArray();
    }
}
