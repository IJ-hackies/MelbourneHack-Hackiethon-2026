using UnityEngine;

/// <summary>
/// Spawns a rising ember/spark particle system from a lamp at runtime.
/// Attach alongside Light2D + TorchLight on each lamp prefab.
/// Set primaryColor and secondaryColor to match the lamp's colour.
/// </summary>
public class LampParticles : MonoBehaviour
{
    [Header("Colours — match your lamp variant")]
    [SerializeField] private Color primaryColor   = new Color(1.00f, 0.35f, 0.05f);
    [SerializeField] private Color secondaryColor = new Color(1.00f, 0.80f, 0.20f);

    [Header("Emission")]
    [SerializeField] private float emissionRate  = 5f;
    [SerializeField] private float riseSpeed     = 0.55f;

    private ParticleSystem _ps;

    private void Start()
    {
        BuildParticleSystem();
    }

    private void BuildParticleSystem()
    {
        // Emit from the sprite's visual centre (top half of bulb) rather than
        // the prefab's root pivot, which is often at the base of the pole.
        var sr = GetComponentInChildren<SpriteRenderer>();
        Vector3 emitWorld = sr != null
            ? sr.bounds.center + new Vector3(0f, sr.bounds.extents.y * 0.4f, 0f)
            : transform.position;

        var go = new GameObject("LampEmbers");
        go.transform.position = emitWorld;   // place at world position first …
        go.transform.SetParent(transform, true); // … then parent, keeping world pos

        _ps = go.AddComponent<ParticleSystem>();
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // ── Renderer ──────────────────────────────────────────────────────────
        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        var psr              = go.GetComponent<ParticleSystemRenderer>();
        psr.material         = new Material(urp != null ? urp : fallback);
        psr.sortingLayerName = "Entities";
        psr.sortingOrder     = 300;

        // ── Main ──────────────────────────────────────────────────────────────
        var main = _ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(riseSpeed * 0.5f, riseSpeed);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.025f, 0.065f);
        main.startColor      = new ParticleSystem.MinMaxGradient(primaryColor, secondaryColor);
        main.gravityModifier = -0.55f;   // particles drift upward
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 40;

        // ── Emission ──────────────────────────────────────────────────────────
        var emission = _ps.emission;
        emission.rateOverTime = emissionRate;

        // ── Shape — tiny disc at lamp head ────────────────────────────────────
        var shape = _ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.07f;

        // ── Velocity over lifetime — gentle horizontal wobble ─────────────────
        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        vel.x       = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        vel.y       = new ParticleSystem.MinMaxCurve(0f);

        // ── Colour + alpha fade over lifetime ─────────────────────────────────
        var col = _ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(secondaryColor, 0.00f),
                new GradientColorKey(primaryColor,   0.40f),
                new GradientColorKey(new Color(
                    primaryColor.r * 0.25f,
                    primaryColor.g * 0.25f,
                    primaryColor.b * 0.25f), 1.00f),
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0.00f),
                new GradientAlphaKey(0.65f, 0.60f),
                new GradientAlphaKey(0.00f, 1.00f),
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        // ── Size shrink over lifetime ─────────────────────────────────────────
        var size = _ps.sizeOverLifetime;
        size.enabled = true;
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 1f);
        sizeCurve.AddKey(1f, 0f);
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        _ps.Play();
    }

    private void OnDestroy()
    {
        if (_ps != null) _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
