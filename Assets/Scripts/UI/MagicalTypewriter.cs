using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Fantasy RPG-style per-character text reveal.
///
/// Each character bounces in (scale 0 → 1.25× → 1.0×) from the previous
/// character's position, flashing bloody red before settling to the base colour.
/// All revealed characters float on a subtle sinusoidal wave.
///
/// Usage:
///   yield return MagicalTypewriter.Reveal(text, fullText, charDelay, baseColor, () => skip);
///
/// Implementation note: vertex manipulation is driven from Canvas.willRenderCanvases
/// so it always runs AFTER TMP's own mesh rebuild — preventing TMP from overwriting
/// our per-character transforms.
/// </summary>
public static class MagicalTypewriter
{
    // ── Tunables ──────────────────────────────────────────────────────────────
    private const float EntryDuration = 0.22f;
    private const float HoldDuration  = 0.80f;
    private const float WaveAmplitude = 2.5f;
    private const float WaveSpeed     = 1.8f;
    private const float WavePhaseStep = 0.50f;

    private static readonly Color BloodRed = new Color(0.80f, 0.04f, 0.04f, 1f);

    // ── Shared state written by the coroutine, read by the canvas callback ────
    private static TMP_Text     s_text;
    private static Vector3[][]  s_orig;
    private static float[]      s_progress;
    private static bool[]       s_started;
    private static Color        s_baseColor;
    private static bool         s_active;   // true while animation is running

    // ── Public API ────────────────────────────────────────────────────────────

    public static IEnumerator Reveal(
        TMP_Text   text,
        string     fullText,
        float      charDelay,
        Color      baseColor,
        Func<bool> skipCheck = null)
    {
        if (string.IsNullOrEmpty(fullText)) yield break;

        Debug.Log($"[MagicalTypewriter] Reveal called — {fullText.Length} chars");

        // ── Layout ────────────────────────────────────────────────────────────
        text.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f);
        text.text  = fullText;
        text.ForceMeshUpdate();

        TMP_TextInfo info      = text.textInfo;
        int          charCount = info.characterCount;

        Debug.Log($"[MagicalTypewriter] charCount={charCount}, meshInfo.Length={info.meshInfo?.Length}");

        if (charCount == 0) { yield break; }

        // ── Snapshot rest-position vertices ───────────────────────────────────
        var orig = new Vector3[charCount][];
        for (int i = 0; i < charCount; i++)
        {
            TMP_CharacterInfo ci = info.characterInfo[i];
            if (!ci.isVisible || ci.materialReferenceIndex >= info.meshInfo.Length)
            {
                orig[i] = null;
                continue;
            }
            Vector3[] src = info.meshInfo[ci.materialReferenceIndex].vertices;
            int vi = ci.vertexIndex;
            if (vi + 3 >= src.Length) { orig[i] = null; continue; }
            orig[i] = new[] { src[vi], src[vi+1], src[vi+2], src[vi+3] };
        }

        // ── Push initial state to shared fields; register canvas callback ─────
        s_text      = text;
        s_orig      = orig;
        s_baseColor = baseColor;
        s_progress  = new float[charCount];
        s_started   = new bool[charCount];
        s_active    = true;

        // HideAll — set vertex colours to transparent so text starts invisible
        HideAll(info);
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);

        // Register our post-rebuild hook so we always run AFTER TMP
        Canvas.willRenderCanvases += OnWillRenderCanvases;

        // ── Reveal loop (coroutine drives TIMING; canvas callback drives VISUALS) ─
        int   nextChar  = 0;
        float charTimer = 0f;

        while (true)
        {
            if (ShouldSkip(skipCheck))
            {
                Cleanup(text, fullText, baseColor);
                yield break;
            }

            float dt = Time.unscaledDeltaTime;

            // Trigger next character
            if (nextChar < charCount)
            {
                charTimer -= dt;
                if (charTimer <= 0f)
                {
                    s_started[nextChar]  = true;
                    s_progress[nextChar] = float.Epsilon;

                    char ch = info.characterInfo[nextChar].character;
                    SFXManager.Instance?.PlayTypewriterTick(ch);

                    charTimer = charDelay;
                    if      (ch == '.' || ch == '!' || ch == '?') charTimer *= 6f;
                    else if (ch == ',')                            charTimer *= 3f;
                    else if (ch == '\n')                           charTimer *= 4f;
                    nextChar++;
                }
            }

            // Advance progress for all animating characters
            bool allSettled = nextChar >= charCount;
            for (int i = 0; i < charCount; i++)
            {
                if (!s_started[i]) continue;
                if (s_progress[i] < 1f)
                {
                    s_progress[i] = Mathf.Min(s_progress[i] + dt / EntryDuration, 1f);
                    if (s_progress[i] < 1f) allSettled = false;
                }
            }

            if (allSettled) break;
            yield return null;
        }

        // ── Wave-only hold ────────────────────────────────────────────────────
        float holdElapsed = 0f;
        while (holdElapsed < HoldDuration)
        {
            if (ShouldSkip(skipCheck))
            {
                Cleanup(text, fullText, baseColor);
                yield break;
            }
            holdElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Cleanup(text, fullText, baseColor);
    }

    // ── Canvas.willRenderCanvases callback ────────────────────────────────────
    // Runs AFTER TMP has rebuilt its own mesh — our writes always win.

    private static void OnWillRenderCanvases()
    {
        if (!s_active || s_text == null || s_orig == null) return;

        TMP_TextInfo info      = s_text.textInfo;
        int          charCount = s_orig.Length;

        for (int i = 0; i < charCount; i++)
        {
            if (!s_started[i]) continue;
            ApplyChar(info, s_orig, i, s_progress[i], s_baseColor);
        }

        s_text.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
    }

    // ── Per-character vertex transform ────────────────────────────────────────

    private static void ApplyChar(
        TMP_TextInfo info, Vector3[][] orig, int i, float t, Color baseColor)
    {
        TMP_CharacterInfo ci = info.characterInfo[i];
        if (!ci.isVisible || orig[i] == null) return;

        int matIdx = ci.materialReferenceIndex;
        if (matIdx >= info.meshInfo.Length) return;

        int       vi     = ci.vertexIndex;
        Vector3[] verts  = info.meshInfo[matIdx].vertices;
        Color32[] colors = info.meshInfo[matIdx].colors32;

        if (vi + 3 >= verts.Length || vi + 3 >= colors.Length) return;

        // Char centre
        Vector3 center = (orig[i][0] + orig[i][1] + orig[i][2] + orig[i][3]) * 0.25f;

        // Scale: 0 → 1.25× → 1.0× (EaseOutBack, clamped ≥ 0)
        float scale = t < 1f ? Mathf.Max(0f, EaseOutBack(t)) : 1f;

        // Slide in from previous char's centre (or from slightly below for first char)
        Vector3 slide = Vector3.zero;
        if (t < 1f)
        {
            float slideT = 1f - EaseOutCubic(t);
            if (i > 0 && orig[i - 1] != null)
            {
                Vector3 prevCenter = (orig[i-1][0] + orig[i-1][1] +
                                      orig[i-1][2] + orig[i-1][3]) * 0.25f;
                slide = (prevCenter - center) * slideT;
            }
            else
            {
                slide = new Vector3(0f, -12f * slideT, 0f);
            }
        }

        // Sinusoidal wave — blends in as char settles
        float waveY = Mathf.Sin(Time.unscaledTime * WaveSpeed + i * WavePhaseStep)
                      * WaveAmplitude * t;

        // Colour: bloody red → base colour, alpha snaps in fast
        float colorT = EaseOutCubic(Mathf.Clamp01(t * 1.6f));
        float alphaT = Mathf.Clamp01(t * 5f);
        Color c = Color.Lerp(BloodRed, baseColor, colorT);
        c.a = alphaT;
        Color32 c32 = c;

        Vector3 waveOffset = new Vector3(0f, waveY, 0f);
        for (int v = 0; v < 4; v++)
        {
            verts[vi + v]  = center + (orig[i][v] - center) * scale + slide + waveOffset;
            colors[vi + v] = c32;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void HideAll(TMP_TextInfo info)
    {
        var hidden = new Color32(0, 0, 0, 0);
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ci = info.characterInfo[i];
            if (!ci.isVisible || ci.materialReferenceIndex >= info.meshInfo.Length) continue;
            Color32[] c  = info.meshInfo[ci.materialReferenceIndex].colors32;
            int       vi = ci.vertexIndex;
            if (vi + 3 < c.Length)
                c[vi] = c[vi+1] = c[vi+2] = c[vi+3] = hidden;
        }
    }

    private static void Cleanup(TMP_Text text, string fullText, Color baseColor)
    {
        s_active = false;
        Canvas.willRenderCanvases -= OnWillRenderCanvases;
        if (text == null) return;
        text.text  = fullText;
        text.color = baseColor;
        text.ForceMeshUpdate();
    }

    private static bool ShouldSkip(Func<bool> f) => f != null && f();

    // EaseOutBack: 0 → ~1.25× overshoot → 1.0
    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
}
