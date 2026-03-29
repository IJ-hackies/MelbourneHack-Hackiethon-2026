using System.Collections;
using UnityEngine;

/// <summary>
/// TETHERED — embeds in the first enemy hit and pulls them toward the caster for 2.5s.
/// Uses a LineRenderer to show the tether rope.
/// </summary>
public class TetheredBehavior : ProjectileBehaviorBase
{
    private bool         active;
    private GameObject   tetheredEnemy;
    private LineRenderer line;

    private const float TetherDuration = 2.5f;
    private const float PullForce      = 14f;

    protected override void OnInitialized()
    {
        // Add LineRenderer dynamically — this component is itself added dynamically
        line               = gameObject.AddComponent<LineRenderer>();
        line.enabled       = false;
        line.positionCount = 2;
        line.startWidth    = 0.05f;
        line.endWidth      = 0.05f;
    }

    public override bool OnHitEnemy(GameObject enemy)
    {
        if (active) return true;
        active        = true;
        tetheredEnemy = enemy;

        ctx.Rb.linearVelocity = Vector2.zero;
        ctx.Rb.bodyType       = RigidbodyType2D.Kinematic;
        transform.SetParent(enemy.transform, true);

        line.enabled = true;
        StartCoroutine(TetherRoutine());
        return true; // suppress destroy
    }

    private IEnumerator TetherRoutine()
    {
        float elapsed = 0f;

        while (elapsed < TetherDuration)
        {
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();

            if (tetheredEnemy == null) break;
            var enemyHealth = tetheredEnemy.GetComponent<Health>();
            if (enemyHealth == null || enemyHealth.IsDead) break;
            if (ctx.CasterTransform == null) break;

            // Pull enemy toward caster
            var enemyRb = tetheredEnemy.GetComponent<Rigidbody2D>();
            if (enemyRb != null)
            {
                Vector2 toCaster = ((Vector2)ctx.CasterTransform.position
                                 - (Vector2)tetheredEnemy.transform.position).normalized;
                enemyRb.AddForce(toCaster * PullForce, ForceMode2D.Force);
            }

            // Update line renderer
            if (line != null && ctx.CasterTransform != null)
            {
                line.SetPosition(0, transform.position);
                line.SetPosition(1, ctx.CasterTransform.position);
            }
        }

        ctx.Handler.RequestDestroy();
    }
}
