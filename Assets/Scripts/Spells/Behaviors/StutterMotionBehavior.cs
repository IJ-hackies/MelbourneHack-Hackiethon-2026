using System.Collections;
using UnityEngine;

/// <summary>STUTTER_MOTION — projectile lurches forward in stop-start bursts.</summary>
public class StutterMotionBehavior : ProjectileBehaviorBase
{
    protected override void OnInitialized() => StartCoroutine(Stutter());

    private IEnumerator Stutter()
    {
        while (ctx != null)
        {
            yield return new WaitForSeconds(0.15f);
            if (ctx == null) yield break;
            ctx.Rb.linearVelocity = Vector2.zero;
            yield return new WaitForSeconds(0.1f);
            if (ctx == null) yield break;
            ctx.Rb.linearVelocity = ctx.InitialDirection * ctx.Spell.speed;
        }
    }
}
