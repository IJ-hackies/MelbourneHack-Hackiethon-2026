using UnityEngine;

/// <summary>
/// WALL_BOUNCE — reflects off walls up to 3 times then destroys.
/// REFLECTING  — reflects off walls indefinitely.
/// </summary>
public class WallBounceBehavior : ProjectileBehaviorBase
{
    private int bounceCount;
    private const int MaxBounces   = 3;
    private const int WallLayer    = 1 << 9;
    private const float LookAhead  = 2f; // frames ahead to cast

    private void FixedUpdate()
    {
        if (ctx == null) return;

        Vector2 nextPos    = ctx.Rb.position + ctx.Rb.linearVelocity * Time.fixedDeltaTime * LookAhead;
        RaycastHit2D hit   = Physics2D.Linecast(ctx.Rb.position, nextPos, WallLayer);
        if (hit.collider == null) return;

        bool unlimited = ctx.HasTag(SpellTag.REFLECTING);
        if (unlimited || bounceCount < MaxBounces)
        {
            bounceCount++;
            ctx.Rb.linearVelocity = Vector2.Reflect(ctx.Rb.linearVelocity, hit.normal);
        }
        else
        {
            ctx.Handler.RequestDestroy();
        }
    }
}
