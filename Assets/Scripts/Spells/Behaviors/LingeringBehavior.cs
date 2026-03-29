using UnityEngine;

/// <summary>
/// LINGERING — spawns a damage zone where the projectile lands (on enemy hit or on destroy).
/// </summary>
public class LingeringBehavior : ProjectileBehaviorBase
{
    private bool zoneSpawned;

    public override bool OnHitEnemy(GameObject enemy)
    {
        SpawnZone(transform.position);
        return false; // let normal destroy proceed
    }

    // Also trigger when destroyed by wall/lifetime with no hit
    private void OnDestroy()
    {
        SpawnZone(transform.position);
    }

    private void SpawnZone(Vector2 pos)
    {
        if (zoneSpawned || ctx == null) return;
        zoneSpawned = true;

        var zoneObj = new GameObject("LingeringZone");
        zoneObj.transform.position = pos;
        var zone = zoneObj.AddComponent<LingeringZone>();
        zone.Init(ctx.Spell.damage * 0.3f, duration: 5f, radius: 2f);
    }
}
