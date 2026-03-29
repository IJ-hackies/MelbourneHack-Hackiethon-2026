using UnityEngine;

/// <summary>BOOMERANG — travels out to max range then returns to the caster. Destroys on return.</summary>
public class BoomerangBehavior : ProjectileBehaviorBase
{
    private float distTraveled;
    private bool  returning;
    private const float MaxRange      = 8f;
    private const float ReturnEpsilon = 0.5f;

    private void FixedUpdate()
    {
        if (ctx == null) return;

        if (!returning)
        {
            distTraveled += ctx.Rb.linearVelocity.magnitude * Time.fixedDeltaTime;
            if (distTraveled >= MaxRange)
            {
                returning             = true;
                ctx.Rb.linearVelocity = Vector2.zero;
            }
        }
        else
        {
            if (ctx.CasterTransform == null) { ctx.Handler.RequestDestroy(); return; }

            Vector2 toCaster = (Vector2)ctx.CasterTransform.position - ctx.Rb.position;
            if (toCaster.magnitude < ReturnEpsilon) { ctx.Handler.RequestDestroy(); return; }

            ctx.Rb.linearVelocity = toCaster.normalized * ctx.Spell.speed;
        }
    }
}
