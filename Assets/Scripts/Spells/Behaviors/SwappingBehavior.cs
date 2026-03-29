using UnityEngine;

/// <summary>SWAPPING — teleports the caster to the projectile's position on enemy impact.</summary>
public class SwappingBehavior : ProjectileBehaviorBase
{
    public override bool OnHitEnemy(GameObject enemy)
    {
        TeleportCaster();
        return false; // destroy normally after teleport
    }

    private void TeleportCaster()
    {
        if (ctx.CasterTransform == null) return;

        ctx.CasterTransform.position = transform.position;

        var casterRb = ctx.CasterTransform.GetComponent<Rigidbody2D>();
        if (casterRb != null)
        {
            casterRb.position = transform.position;
            Physics2D.SyncTransforms();
        }
    }
}
