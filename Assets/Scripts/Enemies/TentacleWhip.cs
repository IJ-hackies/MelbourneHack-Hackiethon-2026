using System.Collections;
using UnityEngine;

// Animated tentacle whip visual. Three phases:
//   1. Coil   — oscillates side-to-side, building tension
//   2. Snap   — extends rapidly toward target (at projectile launch)
//   3. Recoil — wobbles back with damping, then fades
// Spawn via TentacleWhip.Spawn() — no prefab needed.
public class TentacleWhip : MonoBehaviour
{
    private const int Segments = 8;

    public static void Spawn(Vector3 origin, Vector2 direction, Color colorA, Color colorB,
                             float coilDuration, float snapDuration = 0.08f, float recoilDuration = 0.28f)
    {
        var go = new GameObject("FX_TentacleWhip");
        go.transform.position = origin;
        var tw = go.AddComponent<TentacleWhip>();
        tw.Init(direction, colorA, colorB, coilDuration, snapDuration, recoilDuration);
    }

    private LineRenderer lr;
    private Vector2 dir;
    private Vector2 perp;

    private void Init(Vector2 direction, Color colorA, Color colorB,
                      float coilDuration, float snapDuration, float recoilDuration)
    {
        dir  = direction.normalized;
        perp = new Vector2(-dir.y, dir.x);

        lr = gameObject.AddComponent<LineRenderer>();
        lr.positionCount = Segments;
        lr.useWorldSpace = true;
        lr.startWidth    = 0.07f;
        lr.endWidth      = 0.02f;
        lr.textureMode   = LineTextureMode.Stretch;

        // Sorting — render above sprites
        lr.sortingLayerName = "Default";
        lr.sortingOrder     = 20;

        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        lr.material = new Material(urp != null ? urp : fallback);

        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(colorA, 0f), new GradientColorKey(colorB, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),     new GradientAlphaKey(0.6f, 1f) }
        );
        lr.colorGradient = gradient;

        StartCoroutine(Animate(colorA, colorB, coilDuration, snapDuration, recoilDuration));
    }

    private IEnumerator Animate(Color colorA, Color colorB,
                                float coilDuration, float snapDuration, float recoilDuration)
    {
        // Phase 1 — Coil: tentacle oscillates side-to-side, building amplitude
        float t = 0f;
        while (t < coilDuration)
        {
            t += Time.deltaTime;
            float p = t / coilDuration;
            float amp = Mathf.Lerp(0.05f, 0.3f, p);           // amplitude builds
            float len = Mathf.Lerp(0.9f,  0.75f, p);          // extended coil length
            SetPoints(len, amp, oscillationFreq: 2f, phaseOffset: Time.time * 6f, alpha: 0.45f);
            yield return null;
        }

        // Phase 2 — Snap: straightens and shoots forward
        t = 0f;
        while (t < snapDuration)
        {
            t += Time.deltaTime;
            float p = t / snapDuration;
            float amp = Mathf.Lerp(0.3f, 0f,  p);             // snaps straight
            float len = Mathf.Lerp(0.75f, 2.2f, EaseOut(p)); // extends fast
            SetPoints(len, amp, oscillationFreq: 2f, phaseOffset: 0f);
            yield return null;
        }

        // Phase 3 — Recoil: wobbles backward with damping amplitude
        t = 0f;
        while (t < recoilDuration)
        {
            t += Time.deltaTime;
            float p    = t / recoilDuration;
            float amp  = Mathf.Lerp(0.2f, 0f,   p);           // damping wobble
            float len  = Mathf.Lerp(2.2f, 0.2f, EaseOut(p)); // snaps back
            float fade = 1f - p;
            SetPoints(len, amp, oscillationFreq: 3f, phaseOffset: Time.time * 8f, alpha: fade);
            yield return null;
        }

        Destroy(gameObject);
    }

    private void SetPoints(float length, float amplitude, float oscillationFreq,
                           float phaseOffset, float alpha = 1f)
    {
        // Update gradient alpha
        var gradient = lr.colorGradient;
        var alphaKeys = gradient.alphaKeys;
        for (int i = 0; i < alphaKeys.Length; i++)
            alphaKeys[i].alpha *= alpha;
        gradient.SetKeys(gradient.colorKeys, alphaKeys);
        lr.colorGradient = gradient;

        Vector3 origin = transform.position;
        for (int i = 0; i < Segments; i++)
        {
            float t   = (float)i / (Segments - 1);
            float osc = Mathf.Sin(t * Mathf.PI * oscillationFreq + phaseOffset) * amplitude * t;
            lr.SetPosition(i, origin + (Vector3)(dir * (t * length) + perp * osc));
        }
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
}
