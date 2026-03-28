using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Vampire's blood laser.
// Telegraph: two thin edge guides track the player and pulse.
// Lock-on:   tracking stops, guides solidify.
// Fire:      three-layer beam (dark outer / blood-red mid / arterial core) with
//            animated sine-wave wobble on the outer layer and blood drip particles.
public class BloodBeamProjectile : MonoBehaviour
{
    public const float LockOnDuration = 0.30f;
    public const float FireDuration   = RampTime + PeakHoldTime + FadeTime;

    private const float BeamLength   = 28f;
    private const float RampTime     = 0.12f;
    private const float PeakHoldTime = 0.25f;
    private const float FadeTime     = 0.30f;

    // Beam widths — outer is wide and dark, core is narrow and bright
    private const float MaxOuterWidth = 0.55f;
    private const float MaxMidWidth   = 0.28f;
    private const float MaxCoreWidth  = 0.09f;

    // Blood colour palette — no pink, no white
    private static readonly Color OuterColor = new Color(0.30f, 0.00f, 0.00f, 0.88f); // dark maroon
    private static readonly Color MidColor   = new Color(0.68f, 0.01f, 0.01f, 0.92f); // arterial blood
    private static readonly Color CoreColor  = new Color(0.92f, 0.08f, 0.08f, 1.00f); // bright fresh blood
    private static readonly Color EdgeColor  = new Color(0.80f, 0.10f, 0.10f, 1.00f); // telegraph guides

    // Outer glow wave — gives the beam an organic, pressurised-blood look
    private const int   WaveSegments = 14;
    private const float MaxWaveAmp   = 0.055f;

    // ── Public entry point ────────────────────────────────────────────────────

    public static void Spawn(Transform attacker, Vector2 initialDir, float damage,
                             Health playerHealth, PlayerHitEffect playerHitEffect,
                             Transform playerTransform = null,
                             float telegraphDuration = 1.5f)
    {
        var go = new GameObject("FX_BloodBeam");
        go.transform.position = attacker.position;
        go.AddComponent<BloodBeamProjectile>()
          .StartCoroutine(BeamLifecycle(go, attacker, initialDir, damage,
                                        playerHealth, playerHitEffect,
                                        playerTransform, telegraphDuration));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private static IEnumerator BeamLifecycle(GameObject go, Transform attacker,
                                              Vector2 initialDir, float damage,
                                              Health playerHealth, PlayerHitEffect playerHitEffect,
                                              Transform playerTransform, float telegraphDuration)
    {
        Vector2 currentDir = initialDir.normalized;

        var edgeA = MakeLR(MakeChild(go), EdgeColor, 2, sortOrder: 10);
        var edgeB = MakeLR(MakeChild(go), EdgeColor, 2, sortOrder: 10);

        // ── Telegraph — tracks player ────────────────────────────────────────
        for (float t = 0f; t < telegraphDuration; t += Time.deltaTime)
        {
            if (playerTransform != null && attacker != null)
                currentDir = ((Vector2)playerTransform.position - (Vector2)attacker.position).normalized;

            UpdateEdgeLines(edgeA, edgeB, attacker, currentDir, pulsing: true, t: t, alpha: 1f);
            yield return null;
        }

        // ── Lock-on — solidify, stop tracking ────────────────────────────────
        for (float t = 0f; t < LockOnDuration; t += Time.deltaTime)
        {
            UpdateEdgeLines(edgeA, edgeB, attacker, currentDir, pulsing: false, t: 0f,
                            alpha: Mathf.Lerp(0.35f, 0.90f, t / LockOnDuration));
            yield return null;
        }

        edgeA.enabled = false;
        edgeB.enabled = false;

        // ── Fire ─────────────────────────────────────────────────────────────
        Vector3 origin = (Vector2)attacker.position + currentDir * 0.5f;
        Vector3 tip    = origin + (Vector3)(currentDir * BeamLength);

        // Three beam layers
        var outer = MakeLR(MakeChild(go), OuterColor, WaveSegments, sortOrder: 11);
        var mid   = MakeLR(MakeChild(go), MidColor,   2,            sortOrder: 12);
        var core  = MakeLR(MakeChild(go), CoreColor,  2,            sortOrder: 13);

        SetEndpoints(mid,  origin, tip);
        SetEndpoints(core, origin, tip);

        // Blood drip particles — emitted from a box spanning the beam length
        var drips = MakeChild(go);
        drips.transform.SetPositionAndRotation(
            Vector3.Lerp(origin, tip, 0.5f),
            Quaternion.FromToRotation(Vector3.right, (Vector3)currentDir));
        SetupBloodDrips(drips, BeamLength);

        // Ramp up
        for (float t = 0f; t < RampTime; t += Time.deltaTime)
        {
            float f = t / RampTime;
            SetWidth(outer, f * MaxOuterWidth, f * MaxOuterWidth * 0.75f);
            SetWidth(mid,   f * MaxMidWidth,   f * MaxMidWidth   * 0.60f);
            SetWidth(core,  f * MaxCoreWidth,  f * MaxCoreWidth  * 0.40f);
            UpdateBloodWave(outer, origin, tip, f * MaxWaveAmp);
            yield return null;
        }

        // Peak — deal damage
        SetWidth(outer, MaxOuterWidth, MaxOuterWidth * 0.75f);
        SetWidth(mid,   MaxMidWidth,   MaxMidWidth   * 0.60f);
        SetWidth(core,  MaxCoreWidth,  MaxCoreWidth  * 0.40f);

        if (playerHealth != null && !playerHealth.IsDead)
        {
            var filter = new ContactFilter2D();
            filter.useTriggers = true;
            filter.SetLayerMask(Physics2D.AllLayers);

            var hits = new List<RaycastHit2D>();
            Physics2D.Raycast(origin, currentDir, filter, hits, BeamLength);

            foreach (var hit in hits)
            {
                if (hit.collider.gameObject.layer != LayerMask.NameToLayer("Hitbox")) continue;
                playerHealth.TakeDamage(damage);
                HitEffectSpawner.SpawnHit(hit.point,
                    new Color(0.85f, 0.04f, 0.04f, 1f),
                    new Color(0.30f, 0.00f, 0.00f, 1f));
                playerHitEffect?.PlayHitEffect();
                break;
            }
        }

        for (float t = 0f; t < PeakHoldTime; t += Time.deltaTime)
        {
            UpdateBloodWave(outer, origin, tip, MaxWaveAmp);
            yield return null;
        }

        // Fade
        for (float t = 0f; t < FadeTime; t += Time.deltaTime)
        {
            float f = 1f - t / FadeTime;
            SetWidth(outer, f * MaxOuterWidth, f * MaxOuterWidth * 0.75f);
            SetWidth(mid,   f * MaxMidWidth,   f * MaxMidWidth   * 0.60f);
            SetWidth(core,  f * MaxCoreWidth,  f * MaxCoreWidth  * 0.40f);
            SetAlpha(outer, OuterColor, f);
            SetAlpha(mid,   MidColor,   f);
            SetAlpha(core,  CoreColor,  f);
            UpdateBloodWave(outer, origin, tip, f * MaxWaveAmp);
            yield return null;
        }

        Destroy(go);
    }

    // ── Wave animation ────────────────────────────────────────────────────────

    private static void UpdateBloodWave(LineRenderer lr, Vector3 origin, Vector3 tip, float amplitude)
    {
        Vector3 delta = tip - origin;
        Vector3 perp  = new Vector3(-delta.normalized.y, delta.normalized.x, 0f);
        float   phase = Time.time * 9f;
        int     n     = lr.positionCount;

        for (int i = 0; i < n; i++)
        {
            float t    = (float)i / (n - 1);
            float env  = Mathf.Sin(t * Mathf.PI);                           // envelope: 0 at ends, 1 in middle
            float wave = Mathf.Sin(t * Mathf.PI * 3.5f + phase) * amplitude * env;
            lr.SetPosition(i, origin + delta * t + perp * wave);
        }
    }

    // ── Edge guide helpers ────────────────────────────────────────────────────

    private static void UpdateEdgeLines(LineRenderer edgeA, LineRenderer edgeB,
                                         Transform attacker, Vector2 dir,
                                         bool pulsing, float t, float alpha)
    {
        Vector2 perp   = new Vector2(-dir.y, dir.x);
        Vector3 origin = (Vector2)attacker.position;
        Vector3 tip    = origin + (Vector3)(dir * BeamLength);

        float pulse = pulsing ? (Mathf.Sin(t * Mathf.PI * 5f) * 0.5f + 0.5f) : 1f;
        float a     = Mathf.Lerp(0.10f, 0.40f, pulse) * alpha;

        Vector3 off = (Vector3)(perp * (MaxOuterWidth * 0.5f));
        SetLine(edgeA, origin + off, tip + off, 0.025f, a);
        SetLine(edgeB, origin - off, tip - off, 0.025f, a);
    }

    private static void SetLine(LineRenderer lr, Vector3 from, Vector3 to, float width, float alpha)
    {
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.startWidth = width;
        lr.endWidth   = width;
        lr.startColor = new Color(EdgeColor.r, EdgeColor.g, EdgeColor.b, alpha);
        lr.endColor   = new Color(EdgeColor.r, EdgeColor.g, EdgeColor.b, alpha * 0.3f);
    }

    // ── Blood drip particles ──────────────────────────────────────────────────

    private static void SetupBloodDrips(GameObject go, float beamLength)
    {
        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        var mat = new Material(urp != null ? urp : fallback);

        var ps  = go.AddComponent<ParticleSystem>();
        go.GetComponent<ParticleSystemRenderer>().material = mat;

        var main             = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.4f,  1.2f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.80f, 0.00f, 0.00f, 1.0f),
                                   new Color(0.25f, 0.00f, 0.00f, 0.8f));
        main.gravityModifier = 1.5f;   // drips fall
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission         = ps.emission;
        emission.rateOverTime = 55f;

        // Box shape aligned with transform.right (beam direction)
        var shape       = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = new Vector3(beamLength * 0.9f, 0.05f, 0.05f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.9f, 0.05f, 0.05f), 0f),
                    new GradientColorKey(new Color(0.2f, 0.00f, 0.00f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
    }

    // ── General helpers ───────────────────────────────────────────────────────

    private static LineRenderer MakeLR(GameObject go, Color color, int pointCount, int sortOrder)
    {
        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");

        var lr = go.AddComponent<LineRenderer>();
        lr.material         = new Material(urp != null ? urp : fallback);
        lr.positionCount    = pointCount;
        lr.useWorldSpace    = true;
        lr.sortingLayerName = "Entities";
        lr.sortingOrder     = sortOrder;
        lr.startColor       = color;
        lr.endColor         = new Color(color.r, color.g, color.b, color.a * 0.5f);
        lr.startWidth       = 0f;
        lr.endWidth         = 0f;
        return lr;
    }

    private static GameObject MakeChild(GameObject parent)
    {
        var child = new GameObject("lr");
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static void SetEndpoints(LineRenderer lr, Vector3 from, Vector3 to)
    {
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
    }

    private static void SetWidth(LineRenderer lr, float start, float end)
    {
        lr.startWidth = start;
        lr.endWidth   = end;
    }

    private static void SetAlpha(LineRenderer lr, Color baseColor, float alpha)
    {
        lr.startColor = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
        lr.endColor   = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha * 0.5f);
    }
}
