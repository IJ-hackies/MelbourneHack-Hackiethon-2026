using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Two-phase game-over overlay with a bounce-in animation.
///
/// Phase 1 — Death Popup (smaller panel):
///   "YOU DIED" header. Animated loading dots while Gemini generates.
///   Narration typewriters in when ready. "Continue →" button appears after.
///
/// Phase 2 — Game Over Screen (full panel):
///   "GAME OVER" header, full narration text, MAIN MENU / EXIT GAME buttons.
///
/// Call PreloadNarration() immediately on player death (fires Gemini in background).
/// Call Show() once the death cutscene finishes — the popup bounces in and
/// picks up the narration as soon as it arrives.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    [Header("Font — same Alagard TMP font as SettingsUI")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Typewriter speed (seconds per character)")]
    [SerializeField] private float typewriterSpeed = 0.032f;

    // ── Colours — parchment/dark theme matching SettingsUI ─────────────────────
    private static readonly Color ColBg        = new Color(0f,    0f,    0f,    0.88f);
    private static readonly Color ColPanel     = new Color(0.16f, 0.12f, 0.08f, 0.97f);
    private static readonly Color ColHeader    = new Color(0.08f, 0.03f, 0.03f, 1f);
    private static readonly Color ColFooter    = new Color(0.10f, 0.07f, 0.03f, 1f);
    private static readonly Color ColParchment = new Color(0.93f, 0.87f, 0.72f, 1f);
    private static readonly Color ColGold      = new Color(0.85f, 0.65f, 0.15f, 1f);
    private static readonly Color ColGoldDim   = new Color(0.55f, 0.40f, 0.08f, 1f);
    private static readonly Color ColCrimson   = new Color(0.80f, 0.06f, 0.06f, 1f);
    private static readonly Color ColBtnDanger = new Color(0.55f, 0.12f, 0.08f, 1f);

    // ── Phase 1 widgets ────────────────────────────────────────────────────────
    private GameObject  _popupPanel;
    private TMP_Text    _loadingLabel;
    private TMP_Text    _popupNarrationText;
    private GameObject  _continueBtn;

    // ── Phase 2 widgets ────────────────────────────────────────────────────────
    private GameObject  _gameOverPanel;
    private TMP_Text    _gameOverNarration;

    // ── Shared ─────────────────────────────────────────────────────────────────
    private GameObject  _canvas;

    // ── Narration state (set by PreloadNarration, consumed by Show) ────────────
    private string  _pendingNarration;
    private bool    _narrationReady;
    private bool    _waitingForNarration; // UI shown, waiting for Gemini

    // ── Loading animation ──────────────────────────────────────────────────────
    private float _loadingTimer;
    private int   _loadingDots;

    public bool IsOpen { get; private set; }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        BuildCanvas();
        _canvas.SetActive(false);
    }

    private void Update()
    {
        if (!IsOpen) return;
        AnimateLoadingDots();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Fire the Gemini narration request immediately (call on player death,
    /// before the death cutscene plays, so it loads in the background).
    /// </summary>
    public void PreloadNarration(int stageReached, string sessionLogJson, string spellList)
    {
        _pendingNarration    = null;
        _narrationReady      = false;
        _waitingForNarration = false;

        if (GeminiClient.Instance != null && GeminiClient.Instance.HasApiKey)
        {
            string prompt = BuildDeathPrompt(stageReached, sessionLogJson, spellList);
            GeminiClient.Instance.GenerateFreeText(prompt, text =>
            {
                _pendingNarration = !string.IsNullOrEmpty(text) ? StripMarkdown(text) : FallbackMessage(stageReached);
                _narrationReady   = true;

                // If the popup is already visible and waiting, start typewriter now
                if (IsOpen && _waitingForNarration)
                {
                    _waitingForNarration = false;
                    StartCoroutine(BeginTypewriter(_pendingNarration));
                }
            });
        }
        else
        {
            _pendingNarration = FallbackMessage(stageReached);
            _narrationReady   = true;
        }
    }

    /// <summary>
    /// Show the death popup with a bounce-in animation.
    /// Call this after the death cutscene finishes.
    /// </summary>
    public void Show()
    {
        _canvas.SetActive(true);
        IsOpen = true;
        PauseManager.Pause(); // ensure paused (timeScale may already be 0 from cutscene)

        // Phase 1 visible, Phase 2 hidden
        _popupPanel.SetActive(true);
        _gameOverPanel.SetActive(false);

        // Reset Phase 1 widgets
        _loadingLabel.text = "Consulting the Chronicle.";
        _loadingLabel.gameObject.SetActive(true);
        _popupNarrationText.text = "";
        _popupNarrationText.gameObject.SetActive(false);
        _continueBtn.SetActive(false);
        _loadingTimer = 0f;
        _loadingDots  = 0;

        // Bounce the panel in from scale 0
        var popupRT = _popupPanel.GetComponent<RectTransform>();
        popupRT.localScale = Vector3.zero;
        StartCoroutine(BounceInPanel(popupRT));

        // Narration may already be ready (Gemini responded during cutscene)
        if (_narrationReady)
        {
            StartCoroutine(BeginTypewriter(_pendingNarration));
        }
        else
        {
            _waitingForNarration = true; // callback will trigger typewriter
        }
    }

    // ── Typewriter ─────────────────────────────────────────────────────────────

    private IEnumerator BeginTypewriter(string fullText)
    {
        _loadingLabel.gameObject.SetActive(false);
        _popupNarrationText.gameObject.SetActive(true);
        _popupNarrationText.text = "";

        foreach (char c in fullText)
        {
            _popupNarrationText.text += c;
            yield return new WaitForSecondsRealtime(typewriterSpeed);
        }

        _continueBtn.SetActive(false); // ensure hidden, then bounce in
        StartCoroutine(BounceInPanel(_continueBtn.GetComponent<RectTransform>()));
        _continueBtn.SetActive(true);
    }

    // ── Loading dots ───────────────────────────────────────────────────────────

    private void AnimateLoadingDots()
    {
        if (_narrationReady || !_loadingLabel.gameObject.activeSelf) return;

        _loadingTimer += Time.unscaledDeltaTime;
        if (_loadingTimer < 0.45f) return;

        _loadingTimer = 0f;
        _loadingDots  = (_loadingDots + 1) % 4;
        _loadingLabel.text = "Consulting the Chronicle" + new string('.', _loadingDots + 1);
    }

    // ── Continue button ────────────────────────────────────────────────────────

    private void OnContinueClicked()
    {
        _gameOverNarration.text = _pendingNarration ?? "";

        _popupPanel.SetActive(false);
        _gameOverPanel.SetActive(true);

        var goRT = _gameOverPanel.GetComponent<RectTransform>();
        goRT.localScale = Vector3.zero;
        StartCoroutine(BounceInPanel(goRT));
    }

    // ── Bounce-in animation ────────────────────────────────────────────────────

    private IEnumerator BounceInPanel(RectTransform rt)
    {
        const float duration = 0.38f;
        float elapsed = 0f;
        rt.localScale = Vector3.zero;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rt.localScale = Vector3.one * EaseOutBounce(t);
            yield return null;
        }

        rt.localScale = Vector3.one;
    }

    // Penner ease-out bounce
    private static float EaseOutBounce(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;
        if (t < 1f / d1)
            return n1 * t * t;
        if (t < 2f / d1)
            { t -= 1.5f   / d1; return n1 * t * t + 0.75f; }
        if (t < 2.5f / d1)
            { t -= 2.25f  / d1; return n1 * t * t + 0.9375f; }
        t -= 2.625f / d1;
        return n1 * t * t + 0.984375f;
    }

    // ── Prompt ─────────────────────────────────────────────────────────────────

    private static string StripMarkdown(string text)
    {
        // Remove bold/italic markers: **, *, __, _
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*{1,3}|_{1,3}", "");
        // Remove heading markers
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*#{1,6}\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        // Remove bullet/list markers at line starts
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*[-•]\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        return text.Trim();
    }

    private static string BuildDeathPrompt(int stageReached, string sessionLogJson, string spellList)
    {
        string sessionSection = string.IsNullOrEmpty(sessionLogJson)
            ? ""
            : $"\nFinal floor session data: {sessionLogJson}";

        return
$@"You are the Chronicle — the smug, contemptuous narrator of the Grimoire dungeon. A Seeker just died pathetically inside its pages.

Stage they reached: {stageReached}
Spells they owned: {(string.IsNullOrEmpty(spellList) ? "none" : spellList)}{sessionSection}

Roast this Seeker mercilessly. Speak directly to them in second person. Do NOT summarise what they did — instead mock the WAY they played. Were their spells trash? Did they barely make it past stage 1? Were they probably button-mashing? Pick apart their incompetence with cruel, specific wit. Think a villain who finds the whole thing embarrassing to witness. Be mean, be funny, be short. 3-4 sentences maximum. No purple prose. No sympathy. No quotation marks. Plain text only — no asterisks, no markdown, no bullet points.";
    }

    private static string FallbackMessage(int stage)
    {
        if (stage <= 1)
            return "The entry hall. You couldn't even survive the tutorial.\nI've seen houseplants put up a better fight.";
        if (stage <= 3)
            return $"Stage {stage}. Truly inspiring mediocrity.\nThe dungeon didn't even notice you were here.";
        return $"Stage {stage}. I almost respected the effort.\nAlmost.";
    }

    // ── Canvas construction ────────────────────────────────────────────────────

    private void BuildCanvas()
    {
        _canvas = new GameObject("GameOverUI_Canvas");
        _canvas.transform.SetParent(transform, false);

        var canvas          = _canvas.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;

        var scaler                 = _canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _canvas.AddComponent<GraphicRaycaster>();

        // Full-screen backdrop
        var bdRT       = MakeRT("Backdrop", _canvas.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        bdRT.offsetMin = Vector2.zero;
        bdRT.offsetMax = Vector2.zero;
        bdRT.gameObject.AddComponent<Image>().color = ColBg;

        BuildDeathPopup(_canvas.transform);
        BuildGameOverScreen(_canvas.transform);
    }

    // ── Phase 1: Death popup ───────────────────────────────────────────────────

    private void BuildDeathPopup(Transform canvasT)
    {
        const float PW = 680f;
        const float PH = 400f;
        const float HH = 72f;   // header height
        const float FH = 72f;   // footer height

        var popupRT = MakeRT("DeathPopup", canvasT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(PW, PH));
        _popupPanel = popupRT.gameObject;
        _popupPanel.AddComponent<Image>().color = ColPanel;

        // Header
        var hRT = MakeRT("Header", popupRT,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, HH));
        hRT.gameObject.AddComponent<Image>().color = ColHeader;
        MakeText("YouDied", hRT, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "YOU DIED", 42f, ColCrimson, TextAlignmentOptions.Center, FontStyles.Bold);

        // Gold line
        var lineRT = MakeRT("Line", popupRT,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -HH), new Vector2(0f, 2f));
        lineRT.gameObject.AddComponent<Image>().color = ColGold;

        // Content area
        float contentTopY = -(HH + 2f);
        float contentH    = PH - HH - 2f - FH - 1f;
        var   contentRT   = MakeRT("Content", popupRT,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, contentTopY), new Vector2(0f, contentH));

        // Loading label (centered)
        _loadingLabel = MakeText("Loading", contentRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(PW - 80f, 36f),
            "Consulting the Chronicle.", 17f,
            new Color(ColParchment.r, ColParchment.g, ColParchment.b, 0.6f),
            TextAlignmentOptions.Center, FontStyles.Normal);

        // Typewriter narration (hidden until Gemini responds)
        _popupNarrationText = MakeText("Narration", contentRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(PW - 80f, contentH - 16f),
            "", 18f, ColParchment, TextAlignmentOptions.Center, FontStyles.Normal);
        _popupNarrationText.textWrappingMode = TMPro.TextWrappingModes.Normal;
        _popupNarrationText.overflowMode     = TextOverflowModes.Overflow;
        _popupNarrationText.gameObject.SetActive(false);

        // Gold divider above footer
        var divRT = MakeRT("Divider", popupRT,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, FH), new Vector2(0f, 1f));
        divRT.gameObject.AddComponent<Image>().color = ColGoldDim;

        // Footer
        var footerRT = MakeRT("Footer", popupRT,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
            Vector2.zero, new Vector2(0f, FH));
        footerRT.gameObject.AddComponent<Image>().color = ColFooter;

        // Continue button (bounces in after typewriter)
        _continueBtn = BuildButton("Continue", footerRT, Vector2.zero,
            new Vector2(200f, 44f), ColGold, "Continue  \u2192", OnContinueClicked);
        _continueBtn.SetActive(false);
    }

    // ── Phase 2: Game Over screen ──────────────────────────────────────────────

    private void BuildGameOverScreen(Transform canvasT)
    {
        const float PW = 820f;
        const float PH = 480f;
        const float HH = 70f;
        const float FH = 80f;

        var panelRT = MakeRT("GameOverPanel", canvasT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(PW, PH));
        _gameOverPanel = panelRT.gameObject;
        _gameOverPanel.AddComponent<Image>().color = ColPanel;

        // Header
        var hRT = MakeRT("Header", panelRT,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, HH));
        hRT.gameObject.AddComponent<Image>().color = ColHeader;
        MakeText("Title", hRT, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "GAME OVER", 42f, ColCrimson, TextAlignmentOptions.Center, FontStyles.Bold);

        // Gold line
        var lineRT = MakeRT("Line", panelRT,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -HH), new Vector2(0f, 2f));
        lineRT.gameObject.AddComponent<Image>().color = ColGold;

        // Narration area
        float areaTopY = -(HH + 2f);
        float areaH    = PH - HH - 2f - FH - 1f;
        var   areaRT   = MakeRT("NarrationArea", panelRT,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, areaTopY), new Vector2(0f, areaH));

        _gameOverNarration = MakeText("Text", areaRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(PW - 90f, areaH - 24f),
            "", 18f, ColParchment, TextAlignmentOptions.Center, FontStyles.Normal);
        _gameOverNarration.textWrappingMode = TMPro.TextWrappingModes.Normal;
        _gameOverNarration.overflowMode     = TextOverflowModes.Overflow;
        _gameOverNarration.raycastTarget    = false;

        // Gold divider above footer
        var divRT = MakeRT("Divider", panelRT,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, FH), new Vector2(0f, 1f));
        divRT.gameObject.AddComponent<Image>().color = ColGoldDim;

        // Footer
        var footerRT = MakeRT("Footer", panelRT,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
            Vector2.zero, new Vector2(0f, FH));
        footerRT.gameObject.AddComponent<Image>().color = ColFooter;

        const float BtnW  = 210f;
        float       leftX = -(BtnW + 10f);

        BuildButton("MainMenu", footerRT, new Vector2(leftX, 0f),
            new Vector2(BtnW, 44f), ColGoldDim, "MAIN MENU", GoToMainMenu);
        BuildButton("Exit", footerRT, new Vector2(BtnW + 10f, 0f),
            new Vector2(BtnW, 44f), ColBtnDanger, "EXIT GAME", QuitGame);

        _gameOverPanel.SetActive(false);
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    private void GoToMainMenu()
    {
        MusicManager.Instance?.FadeOut();
        PauseManager.Reset();
        SceneManager.LoadScene("Menu");
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private GameObject BuildButton(string name, RectTransform parent, Vector2 pos,
        Vector2 size, Color color, string label, UnityEngine.Events.UnityAction onClick)
    {
        var rt  = MakeRT(name, parent,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;

        MakeText("Label", rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            label, 17f, ColParchment, TextAlignmentOptions.Center, FontStyles.Bold);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        rt.gameObject.AddComponent<UIButtonHover>();

        return rt.gameObject;
    }

    private static RectTransform MakeRT(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go       = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt       = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    private TMP_Text MakeText(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size, string text,
        float fontSize, Color color, TextAlignmentOptions align,
        FontStyles style = FontStyles.Normal)
    {
        var rt = MakeRT(name, parent, anchorMin, anchorMax, pivot, pos, size);
        if (size == Vector2.zero) { rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        var tmp              = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font             = font;
        tmp.text             = text;
        tmp.fontSize         = fontSize;
        tmp.color            = color;
        tmp.alignment        = align;
        tmp.fontStyle        = style;
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.overflowMode     = TextOverflowModes.Overflow;
        tmp.raycastTarget    = false;
        return tmp;
    }
}
