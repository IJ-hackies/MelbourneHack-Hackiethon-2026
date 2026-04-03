using UnityEngine;

/// <summary>
/// Attach to any empty GameObject in the menu scene.
/// Programmatically creates and configures a full-screen snow particle effect.
/// </summary>
public class MenuSnow : MonoBehaviour
{
    [Header("Snow Settings")]
    [SerializeField] private float emissionRate  = 50f;
    [SerializeField] private float lifetime      = 6f;
    [SerializeField] private float fallSpeed     = 2.5f;
    [SerializeField] private float sizeMin       = 0.04f;
    [SerializeField] private float sizeMax       = 0.12f;
    [SerializeField] private float driftStrength = 0.3f;
    [SerializeField] private int   maxParticles  = 300;
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int   orderInLayer  = 10;

    private ParticleSystem _ps;

    private void Awake()
    {
        _ps = gameObject.AddComponent<ParticleSystem>();
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        PositionAboveCamera();
        ConfigureModules();
        ConfigureRenderer();

        _ps.Play();

        // Pre-warm: simulate a full lifetime's worth so flakes are already
        // spread across the screen when the menu first appears.
        _ps.Simulate(lifetime, true, false);
        _ps.Play();
    }

    private void PositionAboveCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float camHalfH = cam.orthographicSize;
        float camHalfW = camHalfH * cam.aspect;

        Vector3 camPos = cam.transform.position;
        transform.position = new Vector3(camPos.x, camPos.y + camHalfH + 0.5f, camPos.z + 1f);

        // Store width for shape module
        _emitterWidth = camHalfW * 2f + 2f; // slight overshoot so edges aren't bare
    }

    private float _emitterWidth;

    private void ConfigureModules()
    {
        // ── Main ──────────────────────────────────────────────────────────────
        var main = _ps.main;
        main.loop             = true;
        main.startLifetime    = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime);
        main.startSpeed       = new ParticleSystem.MinMaxCurve(fallSpeed * 0.7f, fallSpeed);
        main.startSize        = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor       = new ParticleSystem.MinMaxGradient(
                                    new Color(1f, 1f, 1f, 0.6f),
                                    new Color(0.85f, 0.92f, 1f, 0.9f));
        main.gravityModifier  = 0.04f;
        main.simulationSpace  = ParticleSystemSimulationSpace.World;
        main.maxParticles     = maxParticles;

        // ── Emission ──────────────────────────────────────────────────────────
        var emission = _ps.emission;
        emission.enabled         = true;
        emission.rateOverTime    = emissionRate;

        // ── Shape ─────────────────────────────────────────────────────────────
        var shape = _ps.shape;
        shape.enabled    = true;
        shape.shapeType  = ParticleSystemShapeType.Box;
        shape.scale      = new Vector3(_emitterWidth, 0.1f, 0.1f);

        // ── Velocity over Lifetime (horizontal drift) ─────────────────────────
        var vol = _ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space   = ParticleSystemSimulationSpace.World;
        vol.x       = new ParticleSystem.MinMaxCurve(-driftStrength, driftStrength);
        vol.y       = new ParticleSystem.MinMaxCurve(0f, 0f);
        vol.z       = new ParticleSystem.MinMaxCurve(0f, 0f);

        // ── Size over Lifetime (slight shrink at end for fade-out feel) ────────
        var sol = _ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve shrink = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.8f, 1f),
            new Keyframe(1f, 0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, shrink);
    }

    private void ConfigureRenderer()
    {
        var rend = _ps.GetComponent<ParticleSystemRenderer>();

        Material mat = CreateSnowMaterial();
        rend.material         = mat;
        rend.sortingLayerName = sortingLayerName;
        rend.sortingOrder     = orderInLayer;
        rend.renderMode       = ParticleSystemRenderMode.Billboard;

        // Generate a soft white circle texture for round snowflakes
        mat.mainTexture = MakeCircleTexture(64);
    }

    private static Texture2D MakeCircleTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - r + 0.5f;
            float dy = y - r + 0.5f;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float alpha = Mathf.Clamp01(1f - (dist - (r - 1.5f)) / 1.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        tex.Apply();
        return tex;
    }

    private static Material CreateSnowMaterial()
    {
        // Try URP/Built-in particle shader; fall back to Sprites/Default
        string[] shaderCandidates = {
            "Universal Render Pipeline/Particles/Unlit",
            "Particles/Standard Unlit",
            "Legacy Shaders/Particles/Alpha Blended Premultiply",
            "Sprites/Default"
        };

        Shader shader = null;
        foreach (string name in shaderCandidates)
        {
            shader = Shader.Find(name);
            if (shader != null) break;
        }

        if (shader == null)
        {
            Debug.LogError("[MenuSnow] Could not find a particle shader. Assign a material manually.");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        var mat = new Material(shader);
        mat.color = Color.white;
        return mat;
    }
}
