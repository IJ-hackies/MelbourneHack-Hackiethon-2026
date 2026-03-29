using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Attach to any GameObject alongside a Light2D to get organic torch flicker.
/// Works with URP 2D point lights (Free Point or Spot).
/// </summary>
[RequireComponent(typeof(Light2D))]
public class TorchLight : MonoBehaviour
{
    public enum ColorPreset
    {
        WarmTorch,      // amber/orange — fire sconces
        CoolMagic,      // icy blue — arcane crystals
        SicklyGreen,    // acid/poison pools
        BloodRed,       // cursed/demonic torches
        Custom          // use the color fields below
    }

    [Header("Color")]
    public ColorPreset preset = ColorPreset.WarmTorch;
    [Tooltip("Only used when preset = Custom")]
    public Color customColor = Color.white;

    [Header("Intensity")]
    [Range(0f, 2f)]  public float baseIntensity  = 0.9f;
    [Range(0f, 1f)]  public float flickerAmount  = 0.25f;   // max deviation from base
    [Range(0.5f, 8f)] public float flickerSpeed  = 3f;      // Perlin noise scroll speed

    [Header("Radius Pulse (optional)")]
    [Range(0f, 0.5f)] public float radiusPulse   = 0.05f;   // fractional pulse on outer radius

    // ── internals ──────────────────────────────────────────────────────────────
    Light2D _light;
    float   _baseRadius;
    float   _noiseOffset;      // per-instance offset so torches don't sync

    static readonly Color[] PresetColors =
    {
        new Color(1.00f, 0.60f, 0.15f),   // WarmTorch
        new Color(0.40f, 0.70f, 1.00f),   // CoolMagic
        new Color(0.35f, 0.90f, 0.30f),   // SicklyGreen
        new Color(0.90f, 0.15f, 0.10f),   // BloodRed
    };

    void Awake()
    {
        _light = GetComponent<Light2D>();
        _baseRadius  = _light.pointLightOuterRadius;
        _noiseOffset = Random.Range(0f, 100f);

        ApplyPreset();
        _light.intensity = baseIntensity;
    }

    void Update()
    {
        float t = Time.time * flickerSpeed + _noiseOffset;

        // Two octaves of Perlin noise for organic look
        float noise = Mathf.PerlinNoise(t, t * 0.37f) * 2f - 1f;  // –1 … +1
        noise += Mathf.PerlinNoise(t * 2.1f, t * 0.9f + 50f) * 0.4f;
        noise /= 1.4f;  // renormalise back to ≈ –1 … +1

        _light.intensity = Mathf.Max(0f, baseIntensity + noise * flickerAmount);

        if (radiusPulse > 0f)
            _light.pointLightOuterRadius = _baseRadius * (1f + noise * radiusPulse);
    }

    void ApplyPreset()
    {
        _light.color = preset == ColorPreset.Custom
            ? customColor
            : PresetColors[(int)preset];
    }

    // Allow hot-swapping preset at runtime from other scripts (e.g. FloorAssembler)
    public void SetPreset(ColorPreset p)
    {
        preset = p;
        ApplyPreset();
    }
}
