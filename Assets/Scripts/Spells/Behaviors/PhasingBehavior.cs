using System.Collections;
using UnityEngine;

/// <summary>
/// PHASING — alternates the projectile between "phased" (intangible, 25% opacity)
/// and "solid" (collidable, full opacity) every 0.5s.
/// Enemies can only be hit during the solid phase.
/// </summary>
public class PhasingBehavior : ProjectileBehaviorBase
{
    private Collider2D     col;
    private SpriteRenderer sr;
    private const float    PhaseInterval = 0.5f;

    protected override void OnInitialized()
    {
        col = GetComponent<Collider2D>();
        sr  = GetComponentInChildren<SpriteRenderer>();
        StartCoroutine(PhaseLoop());
    }

    private IEnumerator PhaseLoop()
    {
        bool phased = false;
        while (ctx != null)
        {
            yield return new WaitForSeconds(PhaseInterval);
            if (ctx == null) yield break;

            phased = !phased;
            if (col != null) col.enabled = !phased;
            if (sr  != null)
            {
                Color c = sr.color;
                c.a     = phased ? 0.2f : 1f;
                sr.color = c;
            }
        }
    }
}
