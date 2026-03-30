using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays Gemini-generated cutscene sequences as dark, atmospheric narrative moments.
///
/// The cutscene is a full-screen overlay — the game world is NEVER visible.
/// The Chronicle speaks from a dark void with floating particles, colored washes,
/// and dramatic text effects. Gemini controls the text, colors, and timing;
/// the player handles all the visual atmosphere automatically.
///
/// Setup:
///   1. Add to a scene GameObject (or let StageDirector find/create it).
///   2. Optionally assign uiFont in Inspector (falls back to TMP default).
/// </summary>
public class CutscenePlayer : MonoBehaviour
{
    [Header("UI (optional — auto-created if null)")]
    [SerializeField] private TMP_FontAsset uiFont;

    // ── Runtime UI ──────────────────────────────────────────────────────────
    private Canvas     canvas;
    private Image      bgImage;            // solid dark background — game is NEVER visible
    private Image      tintOverlay;        // colored tint layer on top of bg
    private Image      flashOverlay;       // full-screen flash layer
    private TMP_Text   displayText;
    private RectTransform textRT;
    private Image      textGlowImage;      // soft glow behind text
    private GameObject particleHost;       // world-space host for UI particles

    private bool       isPlaying;
    private bool       skipping;
    private List<GameObject> activeParticles = new();

    /// <summary>True while a cutscene sequence is running.</summary>
    public bool IsPlaying => isPlaying;

    // ── Public API ──────────────────────────────────────────────────────────

    public void Play(CutsceneStepDTO[] steps, Action onComplete)
    {
        if (steps == null || steps.Length == 0)
        {
            onComplete?.Invoke();
            return;
        }

        skipping = false;
        StartCoroutine(RunSequence(steps, onComplete));
    }

    public void Skip() => skipping = true;

    // ── Setup ───────────────────────────────────────────────────────────────

    private void EnsureUI()
    {
        if (canvas != null) return;

        var cGO = new GameObject("CutscenePlayerCanvas");
        cGO.transform.SetParent(transform, false);
        canvas = cGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = cGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        cGO.AddComponent<GraphicRaycaster>();

        // Solid dark background — the game world is never visible
        bgImage = CreateFullscreenImage("CutsceneBG", canvas.transform);
        bgImage.color = new Color(0.02f, 0.01f, 0.04f, 1f); // near-black with slight purple

        // Tint overlay — Gemini controls the color wash
        tintOverlay = CreateFullscreenImage("CutsceneTint", canvas.transform);
        tintOverlay.color = new Color(0, 0, 0, 0);

        // Flash overlay
        flashOverlay = CreateFullscreenImage("CutsceneFlash", canvas.transform);
        flashOverlay.color = new Color(1, 1, 1, 0);

        // Text glow — soft radial glow behind the text
        var glowGO = new GameObject("TextGlow");
        glowGO.transform.SetParent(canvas.transform, false);
        textGlowImage = glowGO.AddComponent<Image>();
        textGlowImage.raycastTarget = false;
        textGlowImage.color = new Color(0.4f, 0.15f, 0.7f, 0f);
        var glowRT = textGlowImage.rectTransform;
        glowRT.anchorMin = new Vector2(0.2f, 0.3f);
        glowRT.anchorMax = new Vector2(0.8f, 0.7f);
        glowRT.offsetMin = Vector2.zero;
        glowRT.offsetMax = Vector2.zero;

        // Chronicle text
        var txtGO = new GameObject("CutsceneText");
        txtGO.transform.SetParent(canvas.transform, false);
        displayText = txtGO.AddComponent<TextMeshProUGUI>();
        if (uiFont != null) displayText.font = uiFont;
        displayText.fontSize         = 38;
        displayText.fontStyle        = FontStyles.Italic;
        displayText.alignment        = TextAlignmentOptions.Center;
        displayText.textWrappingMode = TextWrappingModes.Normal;
        displayText.overflowMode     = TextOverflowModes.Overflow;
        displayText.raycastTarget    = false;
        displayText.color            = new Color(0.9f, 0.8f, 0.55f, 0f);
        displayText.text             = "";
        displayText.enableWordWrapping = true;
        textRT = displayText.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0.12f, 0.35f);
        textRT.anchorMax = new Vector2(0.88f, 0.65f);
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
    }

    private Image CreateFullscreenImage(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return img;
    }

    // ── Sequence runner ─────────────────────────────────────────────────────

    private IEnumerator RunSequence(CutsceneStepDTO[] steps, Action onComplete)
    {
        isPlaying = true;
        EnsureUI();
        canvas.gameObject.SetActive(true);

        // ── Pause game, disable input ───────────────────────────────────
        float savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        var playerMove = FindAnyObjectByType<PlayerMovement>();
        if (playerMove != null) playerMove.enabled = false;

        // ── Set initial state: fully opaque dark background ─────────────
        bgImage.color      = new Color(0.02f, 0.01f, 0.04f, 1f);
        tintOverlay.color  = new Color(0, 0, 0, 0);
        flashOverlay.color = new Color(1, 1, 1, 0);
        textGlowImage.color = new Color(0.4f, 0.15f, 0.7f, 0f);
        displayText.text   = "";
        displayText.color  = new Color(0.9f, 0.8f, 0.55f, 0f);

        // ── Start ambient atmosphere: slow-drifting particles ────────────
        Coroutine ambientRoutine = StartCoroutine(AmbientParticleLoop());
        Coroutine glowRoutine    = StartCoroutine(AmbientGlowPulse());
        StartScreenParticles();

        // ── Fade in from pure black ─────────────────────────────────────
        yield return FadeBG(new Color(0, 0, 0, 1f), new Color(0.02f, 0.01f, 0.04f, 1f), 1.0f);

        // ── Execute Gemini-generated steps ───────────────────────────────
        foreach (var step in steps)
        {
            if (skipping) break;
            yield return ExecuteStep(step);
        }

        // ── Cleanup: fade to pure black ─────────────────────────────────
        if (!skipping)
            yield return FadeBG(bgImage.color, Color.black, 0.8f);
        else
            bgImage.color = Color.black;

        // Stop ambient effects
        if (ambientRoutine != null) StopCoroutine(ambientRoutine);
        if (glowRoutine != null)    StopCoroutine(glowRoutine);
        CleanupParticles();

        displayText.text        = "";
        tintOverlay.color       = new Color(0, 0, 0, 0);
        flashOverlay.color      = new Color(1, 1, 1, 0);
        textGlowImage.color     = new Color(0.4f, 0.15f, 0.7f, 0f);

        if (playerMove != null) playerMove.enabled = true;
        Time.timeScale = savedTimeScale;

        canvas.gameObject.SetActive(false);
        isPlaying = false;
        onComplete?.Invoke();
    }

    // ── Ambient atmosphere (always running during cutscene) ──────────────

    private IEnumerator AmbientParticleLoop()
    {
        // Spawn drifting motes on the UI canvas using world-space particle system
        // positioned at the camera so they appear as screen-space particles
        while (!skipping)
        {
            SpawnUIParticles(new Color(0.5f, 0.25f, 0.8f, 0.4f), 3, 6f, 0.5f);
            yield return new WaitForSecondsRealtime(2f);
        }
    }

    private IEnumerator AmbientGlowPulse()
    {
        // Slow breathing pulse on the text glow
        float t = 0f;
        while (!skipping)
        {
            t += Time.unscaledDeltaTime * 0.8f;
            float a = 0.04f + Mathf.Sin(t) * 0.03f;
            textGlowImage.color = new Color(textGlowImage.color.r, textGlowImage.color.g,
                                             textGlowImage.color.b, a);
            yield return null;
        }
    }

    private void StartScreenParticles()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float w = cam.orthographicSize * cam.aspect * 2f;
        float h = cam.orthographicSize * 2f;

        var go = new GameObject("CutsceneScreenParticles");
        go.transform.position = cam.transform.position + Vector3.forward * 5f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // Main — looping, fills the screen continuously
        var main             = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(3.5f, 6.0f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.05f, 0.22f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.55f, 0.25f, 0.90f, 0.55f),
                                   new Color(0.70f, 0.35f, 1.00f, 0.40f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.maxParticles    = 120;
        main.gravityModifier = -0.04f; // very gentle upward float

        // Spread evenly across the full screen
        var emission          = ps.emission;
        emission.rateOverTime = 18f;

        var shape        = ps.shape;
        shape.shapeType  = ParticleSystemShapeType.Box;
        shape.scale      = new Vector3(w, h, 0.1f);

        // Slow lateral wander so particles don't move in straight lines
        var vol     = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.x       = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
        vol.y       = new ParticleSystem.MinMaxCurve( 0.04f, 0.18f);

        // Fade in, hold, fade out — keeps the effect subtle
        var col     = ps.colorOverLifetime;
        col.enabled = true;
        var g       = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.60f, 0.28f, 0.92f), 0f),
                    new GradientColorKey(new Color(0.72f, 0.38f, 1.00f), 0.5f),
                    new GradientColorKey(new Color(0.55f, 0.22f, 0.85f), 1f) },
            new[] { new GradientAlphaKey(0.00f, 0.00f),
                    new GradientAlphaKey(0.55f, 0.15f),
                    new GradientAlphaKey(0.55f, 0.75f),
                    new GradientAlphaKey(0.00f, 1.00f) });
        col.color = g;

        // Gentle shrink toward end of life so particles disappear softly
        var sol     = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.3f), new Keyframe(0.2f, 1f),
            new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));

        ApplyParticleMaterial(ps.GetComponent<ParticleSystemRenderer>());
        ps.Play();

        activeParticles.Add(go);
    }

    // ── Step execution ───────────────────────────────────────────────────

    private IEnumerator ExecuteStep(CutsceneStepDTO step)
    {
        if (step == null || string.IsNullOrEmpty(step.action)) yield break;

        switch (step.action.ToUpperInvariant())
        {
            case "TYPEWRITER":
                yield return DoTypewriter(step.text, step.speed > 0f ? step.speed : 0.05f);
                break;

            case "CLEAR_TEXT":
                yield return DoClearText(step.duration > 0f ? step.duration : 0.6f);
                break;

            case "FLASH":
                yield return DoFlash(ParseColor(step.color, new Color(1f, 0.85f, 0.5f)));
                break;

            case "WAIT":
                yield return DoWait(step.duration > 0f ? step.duration : 1f);
                break;

            case "SCREEN_TINT":
                yield return DoScreenTint(ParseColor(step.color, new Color(0.3f, 0.1f, 0.5f)),
                                          step.duration > 0f ? step.duration : 1.5f);
                break;

            case "PARTICLES_BURST":
                DoParticlesBurst(ParseColor(step.color, new Color(0.8f, 0.4f, 1f)),
                                 step.count > 0 ? step.count : 30);
                break;

            case "PARTICLES_DRIFT":
                SpawnUIParticles(ParseColor(step.color, new Color(0.6f, 0.3f, 0.9f, 0.5f)),
                                 step.count > 0 ? step.count : 15,
                                 step.duration > 0f ? step.duration : 4f,
                                 0.3f);
                break;

            case "TEXT_SHAKE":
                yield return DoTextShake(step.intensity > 0f ? step.intensity : 8f,
                                         step.duration > 0f ? step.duration : 0.5f);
                break;

            case "PULSE":
                yield return DoPulse(step.intensity > 0f ? step.intensity : 0.15f,
                                     step.count > 0 ? step.count : 3,
                                     step.duration > 0f ? step.duration : 2f);
                break;

            case "GLITCH":
                yield return DoGlitch(step.duration > 0f ? step.duration : 0.4f);
                break;

            default:
                Debug.LogWarning($"[CutscenePlayer] Unknown action: {step.action}");
                break;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  BUILDING BLOCKS
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator DoTypewriter(string fullText, float charDelay)
    {
        if (string.IsNullOrEmpty(fullText)) yield break;

        // Fade in the text glow
        StartCoroutine(FadeGlow(0.15f, 0.6f));

        displayText.text = "";
        displayText.color = new Color(displayText.color.r, displayText.color.g,
                                       displayText.color.b, 1f);

        for (int i = 0; i < fullText.Length; i++)
        {
            if (skipping) { displayText.text = fullText; yield break; }
            displayText.text = fullText.Substring(0, i + 1);
            float delay = charDelay;
            char ch = fullText[i];
            SFXManager.Instance?.PlayTypewriterTick(ch);
            if (ch == '.' || ch == '!' || ch == '?') delay *= 6f;
            else if (ch == ',')                       delay *= 3f;
            else if (ch == '\n')                      delay *= 4f;
            yield return new WaitForSecondsRealtime(delay);
        }

        // Hold the text visible for a beat after finishing
        yield return new WaitForSecondsRealtime(0.8f);
    }

    private IEnumerator DoClearText(float duration)
    {
        // Fade out text and glow together
        StartCoroutine(FadeGlow(0f, duration));

        float elapsed = 0f;
        Color c = displayText.color;
        while (elapsed < duration)
        {
            if (skipping) { c.a = 0f; displayText.color = c; displayText.text = ""; yield break; }
            elapsed += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(1f, 0f, elapsed / duration);
            displayText.color = c;
            yield return null;
        }
        c.a = 0f;
        displayText.color = c;
        displayText.text = "";
    }

    private IEnumerator DoFlash(Color flashColor)
    {
        // Sharp flash — snap to bright, then fade out
        flashOverlay.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0.9f);
        yield return new WaitForSecondsRealtime(0.08f);

        float elapsed = 0f;
        float fadeDuration = 0.5f;
        while (elapsed < fadeDuration)
        {
            if (skipping) { flashOverlay.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0); yield break; }
            elapsed += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(0.9f, 0f, EaseOutCubic(elapsed / fadeDuration));
            flashOverlay.color = new Color(flashColor.r, flashColor.g, flashColor.b, a);
            yield return null;
        }
        flashOverlay.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
    }

    private IEnumerator DoWait(float seconds)
    {
        if (skipping) yield break;
        yield return new WaitForSecondsRealtime(seconds);
    }

    private IEnumerator DoScreenTint(Color targetColor, float duration)
    {
        // Crossfade the tint overlay to a new color
        Color startColor = tintOverlay.color;
        Color endColor = new Color(targetColor.r, targetColor.g, targetColor.b, 0.3f);

        float elapsed = 0f;
        float half = duration / 2f;

        // Fade in tint
        while (elapsed < half)
        {
            if (skipping) { tintOverlay.color = new Color(0, 0, 0, 0); yield break; }
            elapsed += Time.unscaledDeltaTime;
            tintOverlay.color = Color.Lerp(startColor, endColor, elapsed / half);
            yield return null;
        }

        // Hold briefly
        yield return new WaitForSecondsRealtime(duration * 0.2f);

        // Fade out tint
        elapsed = 0f;
        while (elapsed < half)
        {
            if (skipping) { tintOverlay.color = new Color(0, 0, 0, 0); yield break; }
            elapsed += Time.unscaledDeltaTime;
            tintOverlay.color = Color.Lerp(endColor, new Color(0, 0, 0, 0), elapsed / half);
            yield return null;
        }
        tintOverlay.color = new Color(0, 0, 0, 0);
    }

    private void DoParticlesBurst(Color color, int count)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        var go = new GameObject("CutsceneBurst");
        go.transform.position = cam.transform.position + Vector3.forward * 5f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main             = ps.main;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.8f, 2.0f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   color,
                                   new Color(color.r * 0.6f, color.g * 0.6f, color.b * 1.2f, color.a));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.maxParticles    = count;
        main.gravityModifier = -0.15f;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape        = ps.shape;
        shape.shapeType  = ParticleSystemShapeType.Sphere;
        shape.radius     = 0.3f;

        var sol     = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.2f), new Keyframe(0.15f, 1f),
            new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));

        var col     = ps.colorOverLifetime;
        col.enabled = true;
        var g       = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(Color.white, 0.3f),
                    new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f),
                    new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        ApplyParticleMaterial(ps.GetComponent<ParticleSystemRenderer>());
        ps.Play();

        activeParticles.Add(go);
        Destroy(go, 3f);
    }

    private void SpawnUIParticles(Color color, int count, float lifetime, float speed)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        var go = new GameObject("CutsceneDrift");
        go.transform.position = cam.transform.position + Vector3.forward * 5f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main             = ps.main;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.02f, 0.07f);
        main.startColor      = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.maxParticles    = count;
        main.gravityModifier = -0.08f;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape        = ps.shape;
        shape.shapeType  = ParticleSystemShapeType.Box;
        shape.scale      = new Vector3(cam.orthographicSize * cam.aspect * 2f,
                                        cam.orthographicSize * 2f, 0.1f);

        var vol     = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.x       = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vol.y       = new ParticleSystem.MinMaxCurve(0.05f,  0.2f);

        var col     = ps.colorOverLifetime;
        col.enabled = true;
        var g       = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.2f),
                    new GradientAlphaKey(0.6f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        ApplyParticleMaterial(ps.GetComponent<ParticleSystemRenderer>());
        ps.Play();

        activeParticles.Add(go);
        Destroy(go, lifetime + 1f);
    }

    private IEnumerator DoTextShake(float intensity, float duration)
    {
        if (textRT == null) yield break;

        Vector2 originalPos = textRT.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (skipping) { textRT.anchoredPosition = originalPos; yield break; }
            elapsed += Time.unscaledDeltaTime;
            float decay = 1f - (elapsed / duration);
            float ox = UnityEngine.Random.Range(-intensity, intensity) * decay;
            float oy = UnityEngine.Random.Range(-intensity, intensity) * decay;
            textRT.anchoredPosition = originalPos + new Vector2(ox, oy);
            yield return null;
        }
        textRT.anchoredPosition = originalPos;
    }

    private IEnumerator DoPulse(float intensity, int pulseCount, float totalDuration)
    {
        float perPulse = totalDuration / Mathf.Max(pulseCount, 1);

        for (int i = 0; i < pulseCount; i++)
        {
            if (skipping) { bgImage.color = new Color(0.02f, 0.01f, 0.04f, 1f); yield break; }

            // Brighten
            float half = perPulse / 2f;
            float elapsed = 0f;
            Color baseColor = bgImage.color;
            Color brightColor = new Color(
                Mathf.Min(baseColor.r + intensity, 0.3f),
                Mathf.Min(baseColor.g + intensity * 0.5f, 0.15f),
                Mathf.Min(baseColor.b + intensity, 0.4f),
                1f);

            while (elapsed < half)
            {
                if (skipping) yield break;
                elapsed += Time.unscaledDeltaTime;
                bgImage.color = Color.Lerp(baseColor, brightColor, EaseInOutCubic(elapsed / half));
                yield return null;
            }

            // Dim back
            elapsed = 0f;
            while (elapsed < half)
            {
                if (skipping) yield break;
                elapsed += Time.unscaledDeltaTime;
                bgImage.color = Color.Lerp(brightColor, baseColor, EaseInOutCubic(elapsed / half));
                yield return null;
            }
            bgImage.color = baseColor;
        }
    }

    private IEnumerator DoGlitch(float duration)
    {
        float elapsed = 0f;
        Color originalBG = bgImage.color;

        while (elapsed < duration)
        {
            if (skipping) { bgImage.color = originalBG; flashOverlay.color = new Color(1, 1, 1, 0); yield break; }

            // Random flicker
            float r = UnityEngine.Random.value;
            if (r < 0.3f)
            {
                // White flash frame
                flashOverlay.color = new Color(1f, 1f, 1f, UnityEngine.Random.Range(0.1f, 0.4f));
            }
            else if (r < 0.6f)
            {
                // Color shift
                bgImage.color = new Color(
                    originalBG.r + UnityEngine.Random.Range(-0.1f, 0.15f),
                    originalBG.g + UnityEngine.Random.Range(-0.05f, 0.1f),
                    originalBG.b + UnityEngine.Random.Range(-0.1f, 0.2f),
                    1f);
            }
            else
            {
                // Normal frame
                bgImage.color = originalBG;
                flashOverlay.color = new Color(1, 1, 1, 0);
            }

            // Offset text briefly
            if (textRT != null && UnityEngine.Random.value < 0.4f)
            {
                textRT.anchoredPosition = new Vector2(
                    UnityEngine.Random.Range(-5f, 5f),
                    UnityEngine.Random.Range(-3f, 3f));
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Restore
        bgImage.color = originalBG;
        flashOverlay.color = new Color(1, 1, 1, 0);
        if (textRT != null) textRT.anchoredPosition = Vector2.zero;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator FadeBG(Color from, Color to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (skipping) { bgImage.color = to; yield break; }
            elapsed += Time.unscaledDeltaTime;
            bgImage.color = Color.Lerp(from, to, EaseInOutCubic(elapsed / duration));
            yield return null;
        }
        bgImage.color = to;
    }

    private IEnumerator FadeGlow(float targetAlpha, float duration)
    {
        Color c = textGlowImage.color;
        float startAlpha = c.a;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            textGlowImage.color = c;
            yield return null;
        }
        c.a = targetAlpha;
        textGlowImage.color = c;
    }

    private void CleanupParticles()
    {
        foreach (var go in activeParticles)
            if (go != null) Destroy(go);
        activeParticles.Clear();
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        if (!hex.StartsWith("#")) hex = "#" + hex;
        return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
    }

    private static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

    private static void ApplyParticleMaterial(ParticleSystemRenderer psr)
    {
        string[] candidates = {
            "Universal Render Pipeline/Particles/Unlit",
            "Particles/Standard Unlit",
            "Sprites/Default",
        };
        Shader shader = null;
        foreach (var s in candidates) { shader = Shader.Find(s); if (shader != null) break; }
        if (shader == null) return;

        var mat = new Material(shader) { color = Color.white };
        var knob = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
        if (knob != null) mat.mainTexture = knob.texture;
        psr.material   = mat;
        psr.renderMode = ParticleSystemRenderMode.Billboard;
    }
}
