using System.Collections;
using UnityEngine;

// Spawned on enemy death — plays the ghost sprite animation while floating up
// and dissolving via a noise-based clip. Runs entirely in a coroutine; no Update().
//
// Noise texture is generated once and shared across all concurrent death effects.
// Each effect gets its own material instance via sharedMaterial so dissolve
// values are independent per ghost.
public class EnemyDeathEffect : MonoBehaviour
{
    private static Texture2D _noiseTex;

    // ── Factory ───────────────────────────────────────────────────────────────

    public static void Spawn(Vector3 position, Sprite[] frames,
                             string sortingLayer, int sortingOrder)
    {
        if (frames == null || frames.Length == 0) return;

        var go = new GameObject("FX_EnemyDeath");
        go.transform.position   = position;
        go.transform.localScale = Vector3.one * 3.5f; // ghost is larger than the source sprite

        go.AddComponent<EnemyDeathEffect>()
          .StartCoroutine(Run(go, frames, sortingLayer, sortingOrder));
    }

    // ── Sequence ──────────────────────────────────────────────────────────────

    private static IEnumerator Run(GameObject go, Sprite[] frames,
                                   string sortingLayer, int sortingOrder)
    {
        var sr              = go.AddComponent<SpriteRenderer>();
        sr.sprite           = frames[0];
        sr.sortingLayerName = sortingLayer;
        sr.sortingOrder     = sortingOrder + 1;

        // Build material and assign via sharedMaterial so we hold the exact
        // instance the renderer uses — avoids the internal clone that .material causes.
        // Ghost uses plain sprite rendering — fades via vertex color alpha
        sr.sharedMaterial = new Material(Shader.Find("Sprites/Default")
                                      ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default"));

        const float Duration      = 1.2f;
        const float FloatHeight   = 0.8f;  // world units to drift upward
        const float DissolveDelay = 0.4f;  // fraction of Duration before fade begins

        Vector3 origin  = go.transform.position;
        int     count   = frames.Length;
        float   elapsed = 0f;

        while (elapsed < Duration)
        {
            float t = elapsed / Duration;

            // Float up — quadratic ease-out (fast start, settles gently)
            float eased = 1f - (1f - t) * (1f - t);
            go.transform.position = origin + Vector3.up * (FloatHeight * eased);

            // Cycle through sprite frames once across the full duration
            int frameIdx = Mathf.Min(Mathf.FloorToInt(t * count), count - 1);
            sr.sprite = frames[frameIdx];

            // Fade: hold opaque for DissolveDelay fraction, then fade out with squared curve
            // (squared keeps it more opaque for longer before dropping off at the end)
            float fadeT = Mathf.Clamp01((t - DissolveDelay) / (1f - DissolveDelay));
            Color c = sr.color;
            c.a      = 1f - fadeT * fadeT;
            sr.color = c;

            elapsed += Time.deltaTime;
            yield return null;
        }

        Object.Destroy(go);
    }

    // ── Noise texture (shared, generated once) ────────────────────────────────

    internal static Texture2D GetNoiseTexture()
    {
        if (_noiseTex != null) return _noiseTex;

        const int size = 64;
        _noiseTex            = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _noiseTex.filterMode = FilterMode.Bilinear;
        _noiseTex.wrapMode   = TextureWrapMode.Repeat;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float n  = Mathf.PerlinNoise(x / (float)size * 4.1f, y / (float)size * 4.1f);
                  n += Mathf.PerlinNoise(x / (float)size * 9.7f, y / (float)size * 9.7f) * 0.4f;
                  n /= 1.4f;
            _noiseTex.SetPixel(x, y, new Color(n, n, n, 1f));
        }
        _noiseTex.Apply();
        return _noiseTex;
    }
}
