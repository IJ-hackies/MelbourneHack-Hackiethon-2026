using UnityEngine;

/// <summary>SPIRAL — rotates velocity vector each FixedUpdate, corkscrewing through the air.</summary>
public class SpiralBehavior : ProjectileBehaviorBase
{
    private const float RotationDegreesPerSecond = 90f;

    private void FixedUpdate()
    {
        if (ctx == null) return;
        float rad = RotationDegreesPerSecond * Mathf.Deg2Rad * Time.fixedDeltaTime;
        Vector2 v = ctx.Rb.linearVelocity;
        ctx.Rb.linearVelocity = new Vector2(
            v.x * Mathf.Cos(rad) - v.y * Mathf.Sin(rad),
            v.x * Mathf.Sin(rad) + v.y * Mathf.Cos(rad));
    }
}
