using System.Collections;
using UnityEngine;

/// <summary>
/// DELAYED_ARC — simulates a top-down arc by scaling the projectile up at its midpoint
/// (the "crest") then accelerating as it "descends".
/// </summary>
public class DelayedArcBehavior : ProjectileBehaviorBase
{
    private const float ArcDuration  = 2f;
    private const float PeakScale    = 1.6f;  // scale at arc apex
    private const float DescentBoost = 1.5f;  // speed multiplier on the way down

    protected override void OnInitialized() => StartCoroutine(ArcRoutine());

    private IEnumerator ArcRoutine()
    {
        float   elapsed   = 0f;
        Vector3 baseScale = transform.localScale;

        while (ctx != null && elapsed < ArcDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / ArcDuration;

            // Parabolic scale peak at t = 0.5
            float scaleBoost = 1f + Mathf.Sin(t * Mathf.PI) * (PeakScale - 1f);
            transform.localScale = baseScale * scaleBoost;

            // Accelerate on the descent half
            if (t > 0.5f)
            {
                float accel = 1f + (t - 0.5f) * 2f * (DescentBoost - 1f);
                ctx.Rb.linearVelocity = ctx.InitialDirection * ctx.Spell.speed * accel;
            }

            yield return null;
        }

        if (transform != null)
            transform.localScale = baseScale;
    }
}
