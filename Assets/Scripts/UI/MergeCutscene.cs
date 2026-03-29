using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen merge ritual cutscene.
/// Add to a GameObject in the scene (alongside MergeRitualUI).
/// Call Play() from MergeRitualUI.OnMergeClick after Grimoire.MergeSpells.
/// </summary>
public class MergeCutscene : MonoBehaviour
{
    [Header("Timing (seconds)")]
    [SerializeField] private float introFadeDuration   = 0.5f;
    [SerializeField] private float minimumSpinDuration = 2.5f;
    [SerializeField] private float mergeFlashDuration  = 0.35f;
    [SerializeField] private float revealDuration      = 0.6f;
    [SerializeField] private float celebrationDuration = 1.2f;
    [SerializeField] private float outroDuration       = 0.5f;

    [Header("Layout")]
    [SerializeField] private float iconSize    = 220f;
    [SerializeField] private float startSpread = 520f;
    [SerializeField] private int   particleCount = 24;

    // Runtime canvas elements
    private GameObject    canvasGO;
    private Image         background;
    private Image         flashOverlay;
    private readonly List<(RectTransform rt, Image img, float dir)> sourceIcons = new();
    private RectTransform mergedIconRT;
    private Image         mergedIconImage;
    private readonly List<(RectTransform rt, Image img)> particles = new();

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// sources:     spell data BEFORE merge (for icons/colors).
    /// merged:      the new SpellData produced by Grimoire.MergeSpells.
    /// isIconReady: polled each frame — spin phase won't end until this returns true.
    ///              Pass () => true if no async icon generation is in flight.
    /// onComplete:  invoked when the cutscene finishes.
    /// </summary>
    public void Play(SpellData[] sources, SpellData merged, Func<bool> isIconReady, Action onComplete)
    {
        if (sources == null || sources.Length < 2) { onComplete?.Invoke(); return; }
        isIconReady ??= () => true;
        StartCoroutine(RunCutscene(sources, merged, isIconReady, onComplete));
    }

    // ── Cutscene coroutine ────────────────────────────────────────────────────

    private IEnumerator RunCutscene(SpellData[] sources, SpellData merged, Func<bool> isIconReady, Action onComplete)
    {
        Build(sources, merged);
        canvasGO.SetActive(true);

        Color[] srcColors  = GetSpellColors(sources);
        Color   mergedColor = ParseColor(merged?.projectileColor, new Color(0.7f, 0.4f, 1f));

        // Per-icon accumulated rotation angles (tracked separately to avoid
        // reading localEulerAngles, which can flip sign unexpectedly)
        float[] angles = new float[sourceIcons.Count];

        // ── Phase 1: Intro — icons fade in ───────────────────────────────────
        SetSourceAlpha(0f);
        float t = 0f;
        while (t < introFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            SetSourceAlpha(Mathf.Clamp01(t / introFadeDuration));
            CycleBackground(srcColors, t);
            yield return null;
        }
        SetSourceAlpha(1f);

        // ── Phase 2: Spin — icons rotate and drift toward center ──────────────
        Vector2[] startPos = GetStartPositions(sourceIcons.Count);
        float spinElapsed  = 0f;

        while (spinElapsed < minimumSpinDuration || !isIconReady())
        {
            spinElapsed += Time.unscaledDeltaTime;
            float progress = spinElapsed / minimumSpinDuration;

            // Approach curve: slow for first 70%, fast for last 30%
            float approach = progress < 0.7f
                ? Mathf.Lerp(0f, 0.2f, progress / 0.7f)
                : Mathf.Lerp(0.2f, 0.88f, (progress - 0.7f) / 0.3f);

            // Spin speed eases from 80 → 400 deg/s
            float spinSpeed = Mathf.Lerp(80f, 400f, progress);

            for (int i = 0; i < sourceIcons.Count; i++)
            {
                angles[i]                    += spinSpeed * sourceIcons[i].dir * Time.unscaledDeltaTime;
                sourceIcons[i].rt.anchoredPosition = Vector2.Lerp(startPos[i], Vector2.zero, approach);
                sourceIcons[i].rt.localEulerAngles = new Vector3(0f, 0f, angles[i]);
            }

            CycleBackground(srcColors, spinElapsed);
            yield return null;
        }

        // ── Phase 3: Flash — icons rush together, white flash ─────────────────
        SFXManager.Instance?.PlayMergeSpell();

        float flashElapsed = 0f;
        while (flashElapsed < mergeFlashDuration)
        {
            flashElapsed += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(flashElapsed / mergeFlashDuration);

            // Rush toward (0,0) in the last bit
            for (int i = 0; i < sourceIcons.Count; i++)
            {
                angles[i] += 720f * sourceIcons[i].dir * Time.unscaledDeltaTime;
                sourceIcons[i].rt.anchoredPosition = Vector2.Lerp(
                    sourceIcons[i].rt.anchoredPosition, Vector2.zero, p * 0.6f + 0.4f);
                sourceIcons[i].rt.localEulerAngles = new Vector3(0f, 0f, angles[i]);
            }

            SetImageAlpha(flashOverlay, p);
            yield return null;
        }

        // Hide source icons, hold white flash
        SetSourceAlpha(0f);
        SetImageAlpha(flashOverlay, 1f);

        // ── Phase 4: Reveal merged icon ───────────────────────────────────────
        // Apply the generated icon now that we know it's ready
        if (merged != null && merged.icon != null)
        {
            mergedIconImage.sprite = merged.icon;
            mergedIconImage.color  = Color.white;
        }

        mergedIconRT.gameObject.SetActive(true);
        mergedIconRT.localScale = Vector3.zero;

        float revealElapsed = 0f;
        while (revealElapsed < revealDuration)
        {
            revealElapsed += Time.unscaledDeltaTime;
            float p     = Mathf.Clamp01(revealElapsed / revealDuration);
            float eased = 1f - Mathf.Pow(1f - p, 3f); // ease-out cubic

            // Punch scale: overshoot to 1.25 then settle to 1
            float scale = eased < 0.65f
                ? Mathf.Lerp(0f, 1.25f, eased / 0.65f)
                : Mathf.Lerp(1.25f, 1f, (eased - 0.65f) / 0.35f);
            mergedIconRT.localScale = Vector3.one * scale;

            // Flash fades out
            SetImageAlpha(flashOverlay, 1f - eased);

            // Background transitions toward merged color
            Color target = new Color(mergedColor.r * 0.35f, mergedColor.g * 0.35f, mergedColor.b * 0.35f, 1f);
            background.color = Color.Lerp(background.color, target, p * 0.08f);

            yield return null;
        }
        mergedIconRT.localScale = Vector3.one;
        SetImageAlpha(flashOverlay, 0f);

        // ── Phase 5: Celebration — particle burst, pulsing background ─────────
        SpawnParticles(mergedColor);

        float celElapsed = 0f;
        while (celElapsed < celebrationDuration)
        {
            celElapsed += Time.unscaledDeltaTime;
            float p = celElapsed / celebrationDuration;
            AnimateParticles(p, mergedColor);

            float pulse = 1f + 0.06f * Mathf.Sin(p * Mathf.PI * 5f);
            background.color = new Color(
                mergedColor.r * 0.35f * pulse,
                mergedColor.g * 0.35f * pulse,
                mergedColor.b * 0.35f * pulse,
                1f);
            yield return null;
        }

        // ── Phase 6: Outro — icon shrinks, background fades ──────────────────
        Color bgAtOutroStart   = background.color;
        Vector3 iconScaleStart = mergedIconRT.localScale;

        float outElapsed = 0f;
        while (outElapsed < outroDuration)
        {
            outElapsed += Time.unscaledDeltaTime;
            float p       = Mathf.Clamp01(outElapsed / outroDuration);
            float eased   = p * p; // ease-in

            mergedIconRT.localScale = iconScaleStart * (1f - eased);

            Color bg = bgAtOutroStart;
            background.color = new Color(bg.r, bg.g, bg.b, bg.a * (1f - eased));

            SetImageAlpha(mergedIconImage, 1f - eased);
            yield return null;
        }

        canvasGO.SetActive(false);
        Cleanup();
        onComplete?.Invoke();
    }

    // ── Canvas builder ────────────────────────────────────────────────────────

    private void Build(SpellData[] sources, SpellData merged)
    {
        if (canvasGO != null) { Destroy(canvasGO); }
        sourceIcons.Clear();
        particles.Clear();

        canvasGO = new GameObject("MergeCutscene_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300; // above MergeRitualUI (220)

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Full-screen background ────────────────────────────────────────────
        var bgRT = MakeFullscreen("Background", canvasGO.transform);
        background = bgRT.gameObject.AddComponent<Image>();
        background.color = Color.black;
        background.raycastTarget = false;

        // ── White flash overlay ───────────────────────────────────────────────
        var flashRT = MakeFullscreen("Flash", canvasGO.transform);
        flashOverlay = flashRT.gameObject.AddComponent<Image>();
        flashOverlay.color = new Color(1f, 1f, 1f, 0f);
        flashOverlay.raycastTarget = false;

        // ── Source spell icons ────────────────────────────────────────────────
        Color[]   srcColors  = GetSpellColors(sources);
        Vector2[] startPositions = GetStartPositions(sources.Length);

        for (int i = 0; i < sources.Length; i++)
        {
            float dir = (i % 2 == 0) ? -1f : 1f; // alternate spin direction

            var rt = MakeRT($"Source_{i}", canvasGO.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                startPositions[i], new Vector2(iconSize, iconSize));

            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;

            if (sources[i] != null && sources[i].icon != null)
            {
                img.sprite = sources[i].icon;
                img.color  = Color.white;
            }
            else
            {
                // No icon: show a colored square as fallback
                img.color = srcColors[i];
            }

            sourceIcons.Add((rt, img, dir));
        }

        // ── Merged icon (hidden until reveal phase) ───────────────────────────
        mergedIconRT = MakeRT("MergedIcon", canvasGO.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(iconSize * 1.35f, iconSize * 1.35f));
        mergedIconImage = mergedIconRT.gameObject.AddComponent<Image>();
        mergedIconImage.raycastTarget = false;

        if (merged != null && merged.icon != null)
        {
            mergedIconImage.sprite = merged.icon;
            mergedIconImage.color  = Color.white;
        }
        else
        {
            Color fallback = ParseColor(merged?.projectileColor, new Color(0.7f, 0.4f, 1f));
            mergedIconImage.color = fallback;
        }
        mergedIconRT.gameObject.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void CycleBackground(Color[] colors, float time)
    {
        if (colors.Length == 0) return;
        float t = Mathf.PingPong(time * 0.6f, 1f);
        Color blended = colors.Length == 1
            ? colors[0]
            : Color.Lerp(colors[0], colors[colors.Length - 1], t);
        background.color = new Color(blended.r * 0.3f, blended.g * 0.3f, blended.b * 0.3f, 1f);
    }

    private void SpawnParticles(Color color)
    {
        for (int i = 0; i < particleCount; i++)
        {
            var rt = MakeRT($"Particle_{i}", canvasGO.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(16f, 16f));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            particles.Add((rt, img));
        }
    }

    private void AnimateParticles(float progress, Color baseColor)
    {
        float maxRadius = 380f;
        for (int i = 0; i < particles.Count; i++)
        {
            float angle  = (i * (360f / particles.Count) + progress * 30f) * Mathf.Deg2Rad;
            float radius = Mathf.SmoothStep(0f, maxRadius, progress);
            particles[i].rt.anchoredPosition = new Vector2(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius);

            // Fade out in second half of celebration
            float alpha = progress < 0.35f ? 1f : Mathf.Lerp(1f, 0f, (progress - 0.35f) / 0.65f);
            particles[i].img.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }
    }

    private void SetSourceAlpha(float a)
    {
        foreach (var (_, img, _) in sourceIcons)
            SetImageAlpha(img, a);
    }

    private void Cleanup()
    {
        if (canvasGO != null) Destroy(canvasGO);
        canvasGO = null;
        sourceIcons.Clear();
        particles.Clear();
    }

    private Vector2[] GetStartPositions(int count)
    {
        float s = startSpread;
        return count switch
        {
            2 => new[] { new Vector2(-s, 0f), new Vector2(s, 0f) },
            3 => new[] { new Vector2(-s, -s * 0.2f), new Vector2(0f, s * 0.45f), new Vector2(s, -s * 0.2f) },
            _ => new[] { new Vector2(-s, 0f), new Vector2(s, 0f) }
        };
    }

    private Color[] GetSpellColors(SpellData[] spells)
    {
        Color[] defaults = { new Color(0.4f, 0.6f, 1f), new Color(1f, 0.45f, 0.35f), new Color(0.4f, 1f, 0.6f) };
        var colors = new Color[spells.Length];
        for (int i = 0; i < spells.Length; i++)
            colors[i] = ParseColor(spells[i]?.projectileColor, defaults[i % defaults.Length]);
        return colors;
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c))
            return c;
        return fallback;
    }

    private static void SetImageAlpha(Image img, float a)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = a;
        img.color = c;
    }

    // ── RectTransform factories ───────────────────────────────────────────────

    private static RectTransform MakeFullscreen(string name, Transform parent)
    {
        var rt = MakeRT(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static RectTransform MakeRT(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }
}
