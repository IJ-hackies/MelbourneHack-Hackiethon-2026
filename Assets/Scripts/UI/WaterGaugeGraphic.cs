using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Custom UI Graphic that renders a vertical fill gauge with an animated
/// water-surface wave at the liquid's top edge.
/// Drop on any RectTransform inside a Canvas.
/// Set fillAmount (0–1) and color from code; tweak wave parameters in Inspector.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class WaterGaugeGraphic : Graphic
{
    [Header("Wave")]
    [Tooltip("Wave height in canvas pixels.")]
    [SerializeField] private float waveAmplitude       = 4.5f;
    [SerializeField] private float wavePrimaryFreq     = 2.2f;   // cycles across width
    [SerializeField] private float wavePrimarySpeed    = 1.6f;   // radians/sec
    [SerializeField] private float waveSecondaryFreq   = 5.8f;
    [SerializeField] private float waveSecondarySpeed  = -3.1f;  // opposite direction

    [Header("Mesh")]
    [SerializeField] private int segments = 48; // horizontal resolution of the wave

    // Set this from code to drive the fill level
    [Range(0f, 1f)]
    public float fillAmount = 0f;

    private void Update()
    {
        // Rebuild the mesh every frame while the gauge is partially filled
        // so the wave surface animates. SetVerticesDirty is lightweight
        // when the canvas hasn't rebuilt yet.
        if (fillAmount > 0.004f)
            SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        float fill = Mathf.Clamp01(fillAmount);
        if (fill <= 0f) return;

        Rect  r    = rectTransform.rect;
        float w    = r.width;
        float h    = r.height;
        float fillH = h * fill;
        float t    = Time.unscaledTime;

        int   n    = Mathf.Max(segments, 4);
        float segW = w / n;

        Color32 c  = color;

        for (int i = 0; i <= n; i++)
        {
            float x     = r.xMin + i * segW;
            float xNorm = (float)i / n;

            // Composite wave only visible when fill is between ~1% and ~99%
            float wave = (fill > 0.01f && fill < 0.99f)
                ? waveAmplitude * (
                    Mathf.Sin(xNorm * Mathf.PI * 2f * wavePrimaryFreq   + t * wavePrimarySpeed)   * 0.65f +
                    Mathf.Sin(xNorm * Mathf.PI * 2f * waveSecondaryFreq + t * waveSecondarySpeed) * 0.35f)
                : 0f;

            float yTop = Mathf.Clamp(r.yMin + fillH + wave, r.yMin, r.yMax);

            // Bottom vertex (flat floor of the liquid column)
            vh.AddVert(new Vector3(x, r.yMin, 0f), c, Vector2.zero);
            // Top vertex (wavy surface)
            vh.AddVert(new Vector3(x, yTop,   0f), c, Vector2.zero);
        }

        // Stitch quads from left to right
        for (int i = 0; i < n; i++)
        {
            int bl = i * 2;
            int tl = i * 2 + 1;
            int br = (i + 1) * 2;
            int tr = (i + 1) * 2 + 1;

            vh.AddTriangle(bl, tl, tr);
            vh.AddTriangle(bl, tr, br);
        }
    }
}
