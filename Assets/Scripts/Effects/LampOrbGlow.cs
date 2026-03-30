using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Adds a small, concentrated Point Light 2D directly on a lamp's orb.
/// Sits right on the sprite rather than illuminating the surrounding area —
/// that job belongs to TorchLight.
///
/// Setup: add to each lamp prefab variant and set orbColor to match the orb.
/// </summary>
public class LampOrbGlow : MonoBehaviour
{
    [Header("Orb colour — match the sprite")]
    [SerializeField] private Color orbColor      = new Color(1.00f, 0.30f, 0.10f);

    [Header("Light shape")]
    [SerializeField] private float intensity     = 2.2f;
    [SerializeField] private float outerRadius   = 0.55f;
    [SerializeField] private float innerRadius   = 0.10f;

    [Header("Subtle pulse")]
    [SerializeField] private float pulseAmount   = 0.18f;   // max deviation from base intensity
    [SerializeField] private float pulseSpeed    = 2.8f;    // Perlin scroll speed

    private Light2D _light;
    private float   _noiseOffset;
    private float   _baseIntensity;

    private void Start()
    {
        // Place the glow at the sprite's visual centre (the orb),
        // regardless of where the prefab root pivot sits.
        var sr        = GetComponentInChildren<SpriteRenderer>();
        Vector3 world = sr != null ? sr.bounds.center : transform.position;

        var go = new GameObject("OrbGlow");
        go.transform.position = world;
        go.transform.SetParent(transform, true); // keep world position

        _light = go.AddComponent<Light2D>();
        _light.lightType             = Light2D.LightType.Point;
        _light.color                 = orbColor;
        _light.intensity             = intensity;
        _light.pointLightOuterRadius = outerRadius;
        _light.pointLightInnerRadius = innerRadius;

        _baseIntensity = intensity;
        _noiseOffset   = Random.Range(0f, 100f);
    }

    private void Update()
    {
        if (_light == null) return;

        float t     = Time.time * pulseSpeed + _noiseOffset;
        float noise = Mathf.PerlinNoise(t, t * 0.43f) * 2f - 1f;
        _light.intensity = Mathf.Max(0f, _baseIntensity + noise * pulseAmount);
    }
}
