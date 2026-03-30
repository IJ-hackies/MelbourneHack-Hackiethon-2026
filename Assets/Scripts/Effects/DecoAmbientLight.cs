using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Adds a small soft Point Light 2D to a decoration at runtime so it isn't
/// lost in the dungeon darkness. Attach to any deco prefab root.
/// No flicker — just a gentle constant ambient glow.
/// </summary>
public class DecoAmbientLight : MonoBehaviour
{
    [SerializeField] private Color  lightColor   = new Color(1f, 0.82f, 0.55f); // warm parchment
    [SerializeField] private float  intensity    = 0.03f;
    [SerializeField] private float  outerRadius  = 10.0f;
    [SerializeField] private float  innerRadius  = 0.5f;

    private void Start()
    {
        var light = gameObject.AddComponent<Light2D>();
        light.lightType             = Light2D.LightType.Point;
        light.color                 = lightColor;
        light.intensity             = intensity;
        light.pointLightOuterRadius = outerRadius;
        light.pointLightInnerRadius = innerRadius;
    }
}
