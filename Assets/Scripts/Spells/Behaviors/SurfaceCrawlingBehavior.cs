using UnityEngine;

/// <summary>
/// SURFACE_CRAWLING — instead of bouncing or destroying on wall contact,
/// the projectile rotates to slide along the wall surface.
/// </summary>
public class SurfaceCrawlingBehavior : ProjectileBehaviorBase
{
    private const int   WallLayer  = 1 << 9;
    private const float LookAhead  = 3f;

    private void FixedUpdate()
    {
        if (ctx == null) return;

        Vector2      nextPos = ctx.Rb.position + ctx.Rb.linearVelocity * Time.fixedDeltaTime * LookAhead;
        RaycastHit2D hit     = Physics2D.Linecast(ctx.Rb.position, nextPos, WallLayer);
        if (hit.collider == null) return;

        // Project velocity onto the wall surface (perpendicular to the hit normal)
        Vector2 along = Vector2.Perpendicular(hit.normal);
        // Pick the perpendicular direction closest to our current travel direction
        if (Vector2.Dot(along, ctx.Rb.linearVelocity) < 0f) along = -along;
        ctx.Rb.linearVelocity = along * ctx.Spell.speed;
    }
}
