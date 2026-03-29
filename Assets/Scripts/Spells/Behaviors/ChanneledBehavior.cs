using UnityEngine;

/// <summary>
/// CHANNELED — while LMB is held the projectile steers toward the mouse cursor.
/// Destroys on mouse release.
/// </summary>
public class ChanneledBehavior : ProjectileBehaviorBase
{
    private void Update()
    {
        if (ctx == null) return;

        if (!Input.GetMouseButton(0))
        {
            ctx.Handler.RequestDestroy();
            return;
        }

        if (Camera.main == null) return;
        Vector3 mouseWorld    = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 toMouse       = ((Vector2)mouseWorld - ctx.Rb.position).normalized;
        ctx.Rb.linearVelocity = toMouse * ctx.Spell.speed;
    }
}
