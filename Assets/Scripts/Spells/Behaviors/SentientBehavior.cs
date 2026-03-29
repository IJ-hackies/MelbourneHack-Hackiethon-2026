using System.Collections;
using System.Collections.Generic;
using Pathfinding;
using UnityEngine;

/// <summary>
/// SENTIENT — AI-controlled projectile that pathfinds toward the nearest enemy.
/// Recomputes its path every 0.5s. Falls back to direct-line movement if A* unavailable.
/// </summary>
public class SentientBehavior : ProjectileBehaviorBase
{
    private EnemyBase        currentTarget;
    private List<Vector3>    waypoints;
    private int              waypointIndex;
    private Seeker           seeker;

    protected override void OnInitialized()
    {
        seeker = gameObject.AddComponent<Seeker>();
        StartCoroutine(PathLoop());
    }

    private IEnumerator PathLoop()
    {
        while (ctx != null)
        {
            RefreshTarget();

            if (currentTarget != null && AstarPath.active != null && seeker != null)
            {
                seeker.StartPath(ctx.Rb.position, currentTarget.transform.position, OnPathComplete);
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    private void OnPathComplete(Pathfinding.Path p)
    {
        if (p.error) return;
        waypoints     = p.vectorPath;
        waypointIndex = 0;
    }

    private void FixedUpdate()
    {
        if (ctx == null) return;

        if (currentTarget == null || currentTarget.IsDead)
        {
            RefreshTarget();
            if (currentTarget == null) return;
        }

        Vector2 dir = GetPathDirection();
        if (dir == Vector2.zero)
            dir = ((Vector2)currentTarget.transform.position - ctx.Rb.position).normalized;

        ctx.Rb.linearVelocity = dir * ctx.Spell.speed;
    }

    private Vector2 GetPathDirection()
    {
        if (waypoints == null || waypoints.Count == 0) return Vector2.zero;

        const float ReachedDist = 0.3f;
        while (waypointIndex < waypoints.Count &&
               Vector2.Distance(ctx.Rb.position, waypoints[waypointIndex]) < ReachedDist)
            waypointIndex++;

        if (waypointIndex >= waypoints.Count) return Vector2.zero;
        return ((Vector2)waypoints[waypointIndex] - ctx.Rb.position).normalized;
    }

    private void RefreshTarget()
    {
        EnemyBase[] all = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);
        currentTarget   = null;
        float minDist   = float.MaxValue;

        foreach (var e in all)
        {
            if (e.IsDead) continue;
            float d = Vector2.Distance(ctx.Rb.position, e.transform.position);
            if (d < minDist) { minDist = d; currentTarget = e; }
        }
    }
}
