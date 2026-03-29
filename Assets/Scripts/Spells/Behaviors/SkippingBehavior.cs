using System.Collections;
using UnityEngine;

/// <summary>
/// SKIPPING — projectile "skips" like a stone: periodic scale pulses with a slight
/// random direction deviation each bounce.
/// </summary>
public class SkippingBehavior : ProjectileBehaviorBase
{
    private const float SkipInterval       = 0.4f;
    private const float DirectionVariance  = 15f; // max degrees of random deflection per skip

    protected override void OnInitialized() => StartCoroutine(SkipLoop());

    private IEnumerator SkipLoop()
    {
        while (ctx != null)
        {
            yield return new WaitForSeconds(SkipInterval);
            if (ctx == null) yield break;

            // Slight random deflection
            float angle = Random.Range(-DirectionVariance, DirectionVariance) * Mathf.Deg2Rad;
            Vector2 v   = ctx.Rb.linearVelocity;
            ctx.Rb.linearVelocity = new Vector2(
                v.x * Mathf.Cos(angle) - v.y * Mathf.Sin(angle),
                v.x * Mathf.Sin(angle) + v.y * Mathf.Cos(angle));

            // Scale pulse
            StartCoroutine(ScalePulse());
        }
    }

    private IEnumerator ScalePulse()
    {
        Vector3 baseScale = transform.localScale;
        const float PulseDuration = 0.18f;
        float t = 0f;
        while (t < PulseDuration && ctx != null)
        {
            t += Time.deltaTime;
            transform.localScale = baseScale * (1f + Mathf.Sin(t / PulseDuration * Mathf.PI) * 0.5f);
            yield return null;
        }
        if (transform != null) transform.localScale = baseScale;
    }
}
