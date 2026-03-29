using System.Collections;
using UnityEngine;

/// <summary>
/// BURROWING — after 0.5s of travel the projectile "goes underground":
/// sprite hidden, collider disabled, continues in the same direction.
/// When it gets within eruption range of an enemy it resurfaces with 1.5× AoE damage.
/// </summary>
public class BurrowingBehavior : ProjectileBehaviorBase
{
    private bool         burrowed;
    private SpriteRenderer sr;
    private Collider2D   col;

    private const float BurrowDelay  = 0.5f;
    private const float EruptRadius  = 2.5f;

    protected override void OnInitialized()
    {
        sr  = GetComponentInChildren<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        StartCoroutine(BurrowDelay_());
    }

    private IEnumerator BurrowDelay_()
    {
        yield return new WaitForSeconds(BurrowDelay);
        if (ctx == null) yield break;

        burrowed = true;
        if (sr  != null) sr.enabled  = false;
        if (col != null) col.enabled = false;
    }

    private void FixedUpdate()
    {
        if (ctx == null || !burrowed) return;

        Collider2D[] nearby = Physics2D.OverlapCircleAll(ctx.Rb.position, EruptRadius, ProjectileHandler.EnemyMask);
        if (nearby.Length == 0) return;

        // Erupt!
        burrowed = false;
        if (sr  != null) sr.enabled  = true;
        if (col != null) col.enabled = true;

        foreach (var c in nearby)
        {
            var h = c.GetComponentInParent<Health>();
            if (h != null && !h.IsDead)
                h.TakeDamage(ctx.Spell.damage * 1.5f);
        }

        ctx.Handler.RequestDestroy();
    }
}
