using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full-screen scroll overlay shown between floors (2 pages).
/// Page 1: Chronicle message.  Page 2: New spell, corruptions, stat changes.
///
/// Call Show(manifest, prevHp, newHp) from StageLoader to display.
/// Setup: Create empty GameObject, attach this script, assign sprites + font.
/// </summary>
public class StageTransitionUI : MonoBehaviour
{
    [Header("Sprites")]
    [SerializeField] private Sprite scrollBg;       // ScrollBackground.png
    [SerializeField] private Sprite nextPageArrow;  // nextPage.png
    [SerializeField] private Sprite boxSprite;      // box.png
    [SerializeField] private Sprite buttonSprite;   // button.png

    [Header("Font")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Layout (tweak in Inspector)")]
    [Tooltip("Scroll width as fraction of screen width (0-1)")]
    [SerializeField] [Range(0.3f, 0.9f)] private float scrollWidthPct = 0.5f;
    [Tooltip("Scroll height as fraction of screen height (0-1)")]
    [SerializeField] [Range(0.4f, 0.98f)] private float scrollHeightPct = 0.88f;
    [SerializeField] private float titleFontSize = 72f;
    [SerializeField] private float messageFontSize = 64f;
    [SerializeField] private float spellNameFontSize = 60f;
    [SerializeField] private float bodyFontSize = 40f;

    [Header("Transition Timing")]
    [SerializeField] private float fadeToBlackDuration = 0.7f;
    [SerializeField] private float stageClearedFadeIn = 0.6f;
    [SerializeField] private float stageClearedHold = 1.0f;
    [SerializeField] private float stageClearedFadeOut = 0.4f;
    [SerializeField] private float messageFadeInDuration = 1.2f;
    [SerializeField] private float messageFadeInDelay = 0.4f;

    private GameObject canvasGO;
    private GameObject page1;
    private GameObject page2;
    private RectTransform _page1ScrollRT;
    private RectTransform _page2ScrollRT;
    private Action onComplete;

    // Page 1 elements
    private TMP_Text titleText;
    private TMP_Text messageText;
    private TMP_Text attributionText;

    // Page 2 elements
    private Image    spellIconImage;
    private TMP_Text brewingText;
    private Coroutine brewingCoroutine;
    private SpellData pendingSpellData;
    private TMP_Text spellNameText;
    private TMP_Text spellFlavorText;
    private TMP_Text spellTagsText;
    private TMP_Text spellStatsText;
    private TMP_Text corruptionText;
    private TMP_Text hpDeltaText;
    private RectTransform corruptionSection;

    // Fade overlay + stage cleared text
    private Image fadeOverlay;
    private TMP_Text stageClearedText;
    private Coroutine fadeCoroutine;

    public bool IsOpen => canvasGO != null && canvasGO.activeSelf;

    /// <summary>
    /// Begin the fade-to-black sequence. Call this when the floor is cleared.
    /// Once fully black, the scroll will appear and the message fades in.
    /// </summary>
    public void FadeToBlackThenShow(FloorManifestDTO manifest, float prevHp, float newHp, Action onDone = null)
    {
        onComplete = onDone;
        if (canvasGO == null) Build();

        // Ensure canvas is active but pages hidden during fade
        canvasGO.SetActive(true);
        page1.SetActive(false);
        page2.SetActive(false);

        Populate(manifest, prevHp, newHp);

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeToBlackSequence());
    }

    /// <summary>Show the transition screen immediately (no fade). onDone is called when the player clicks Begin.</summary>
    public void Show(FloorManifestDTO manifest, float prevHp, float newHp, Action onDone = null)
    {
        onComplete = onDone;
        if (canvasGO == null) Build();
        canvasGO.SetActive(true);
        page1.SetActive(true);
        page2.SetActive(false);
        PauseManager.Pause();
        SFXManager.Instance?.PlayScrollOpen();
        Populate(manifest, prevHp, newHp);
        if (_page1ScrollRT != null)
            StartCoroutine(UIPanelAnimator.AnimateIn(_page1ScrollRT));
    }

    public void Hide()
    {
        if (Grimoire.Instance != null)
            Grimoire.Instance.OnLoadoutChanged -= RefreshSpellIcon;
        if (brewingCoroutine != null) { StopCoroutine(brewingCoroutine); brewingCoroutine = null; }
        pendingSpellData = null;

        if (canvasGO != null) canvasGO.SetActive(false);
        if (fadeOverlay != null) fadeOverlay.color = new Color(0f, 0f, 0f, 0f);
        PauseManager.Unpause();
        onComplete?.Invoke();
        onComplete = null;
    }

    private IEnumerator FadeToBlackSequence()
    {
        // Fade music out alongside the screen
        MusicManager.Instance?.FadeOut();

        // Phase 1: Fade the screen to black (game still running)
        fadeOverlay.color = new Color(0f, 0f, 0f, 0f);
        fadeOverlay.raycastTarget = true; // block input during fade
        SetTextAlpha(stageClearedText, 0f);

        float elapsed = 0f;
        while (elapsed < fadeToBlackDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeToBlackDuration);
            float alpha = t * t;
            fadeOverlay.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }
        fadeOverlay.color = Color.black;
        PauseManager.Pause();

        // Phase 2: "STAGE CLEARED" fade in, hold, fade out
        elapsed = 0f;
        while (elapsed < stageClearedFadeIn)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / stageClearedFadeIn);
            SetTextAlpha(stageClearedText, t);
            yield return null;
        }
        SetTextAlpha(stageClearedText, 1f);

        yield return new WaitForSecondsRealtime(stageClearedHold);

        elapsed = 0f;
        while (elapsed < stageClearedFadeOut)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / stageClearedFadeOut);
            SetTextAlpha(stageClearedText, 1f - t);
            yield return null;
        }
        SetTextAlpha(stageClearedText, 0f);

        // Phase 3: Show scroll, fade in chronicle text
        page1.SetActive(true);
        SFXManager.Instance?.PlayScrollOpen();
        page2.SetActive(false);
        if (_page1ScrollRT != null)
            StartCoroutine(UIPanelAnimator.AnimateIn(_page1ScrollRT));

        SetTextAlpha(titleText, 0f);
        SetTextAlpha(messageText, 0f);
        SetTextAlpha(attributionText, 0f);

        yield return new WaitForSecondsRealtime(messageFadeInDelay);

        elapsed = 0f;
        while (elapsed < messageFadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / messageFadeInDuration);
            float alpha = 1f - (1f - t) * (1f - t);
            SetTextAlpha(titleText, alpha);
            SetTextAlpha(messageText, alpha);
            SetTextAlpha(attributionText, alpha * 0.7f);
            yield return null;
        }
        SetTextAlpha(titleText, 1f);
        SetTextAlpha(messageText, 1f);
        SetTextAlpha(attributionText, 0.7f);

        fadeCoroutine = null;
    }

    private static void SetTextAlpha(TMP_Text text, float alpha)
    {
        if (text == null) return;
        var c = text.color;
        c.a = alpha;
        text.color = c;
    }

    // ── Build ────────────────────────────────────────────────────────────────

    private void Build()
    {
        canvasGO = new GameObject("StageTransition_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Full-screen fade overlay (used for fade-to-black transition)
        var fadeRT = MakeRT("FadeOverlay", canvasGO.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        fadeRT.offsetMin = Vector2.zero;
        fadeRT.offsetMax = Vector2.zero;
        fadeOverlay = fadeRT.gameObject.AddComponent<Image>();
        fadeOverlay.color = new Color(0f, 0f, 0f, 0f);
        fadeOverlay.raycastTarget = false;

        // "STAGE CLEARED" text centered on screen (shown during black screen)
        stageClearedText = MakeTMP("StageClearedText", (RectTransform)canvasGO.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(800f, 100f),
            "STAGE CLEARED", 80f, Color.white, TextAlignmentOptions.Center);
        stageClearedText.fontStyle = FontStyles.Bold;
        SetTextAlpha(stageClearedText, 0f);

        BuildPage1();
        BuildPage2();
    }

    private void BuildPage1()
    {
        page1 = new GameObject("Page1");
        page1.transform.SetParent(canvasGO.transform, false);
        var p1rt = page1.AddComponent<RectTransform>();
        p1rt.anchorMin = Vector2.zero; p1rt.anchorMax = Vector2.one;
        p1rt.offsetMin = Vector2.zero; p1rt.offsetMax = Vector2.zero;

        // Scroll background — sized as percentage of screen via anchors
        float halfW = scrollWidthPct / 2f;
        float halfH = scrollHeightPct / 2f;
        var scrollRT = MakeRT("Scroll", page1.transform,
            new Vector2(0.5f - halfW, 0.5f - halfH),
            new Vector2(0.5f + halfW, 0.5f + halfH),
            new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        scrollRT.offsetMin = Vector2.zero;
        scrollRT.offsetMax = Vector2.zero;
        var scrollImg = scrollRT.gameObject.AddComponent<Image>();
        scrollImg.sprite = scrollBg;
        scrollImg.type = Image.Type.Sliced;
        _page1ScrollRT = scrollRT;

        // ── Vertically centered content container (anchor-based) ──
        var contentRT = MakeRT("Content", scrollRT,
            new Vector2(0.1f, 0.15f), new Vector2(0.9f, 0.85f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;

        // Title — top of content block
        titleText = MakeTMP("Title", contentRT,
            new Vector2(0f, 0.82f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, Vector2.zero,
            "", 68f, Color.black, TextAlignmentOptions.Center);
        titleText.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        titleText.GetComponent<RectTransform>().offsetMax = Vector2.zero;

        // Divider
        var divRT = MakeRT("Divider", contentRT,
            new Vector2(0.15f, 0.78f), new Vector2(0.85f, 0.78f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(0f, 2f));
        divRT.offsetMin = new Vector2(divRT.offsetMin.x, 0f);
        divRT.offsetMax = new Vector2(divRT.offsetMax.x, 2f);
        var divImg = divRT.gameObject.AddComponent<Image>();
        divImg.color = new Color(0.58f, 0.42f, 0.18f, 0.5f);

        // Chronicle message — fills area between divider and bottom
        messageText = MakeTMP("Message", contentRT,
            new Vector2(0.10f, 0f), new Vector2(0.90f, 0.75f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "", Mathf.Max(messageFontSize, 40f), new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);
        messageText.fontStyle = FontStyles.Italic;
        messageText.textWrappingMode = TMPro.TextWrappingModes.Normal;
        messageText.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        messageText.GetComponent<RectTransform>().offsetMax = Vector2.zero;

        // Attribution — near bottom of scroll
        attributionText = MakeTMP("Attribution", scrollRT,
            new Vector2(0.5f, 0.05f), new Vector2(0.5f, 0.05f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(400f, 50f),
            "— The Chronicle", 32f, new Color(0.53f, 0.18f, 0.61f, 0.7f), TextAlignmentOptions.Center);

        // Next page button
        var nextRT = MakeRT("NextBtn", scrollRT,
            new Vector2(0.88f, 0.10f), new Vector2(0.88f, 0.10f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(72f, 72f));
        var nextImg = nextRT.gameObject.AddComponent<Image>();
        nextImg.sprite = nextPageArrow;
        nextImg.preserveAspect = true;
        var nextBtn = nextRT.gameObject.AddComponent<Button>();
        nextBtn.targetGraphic = nextImg;
        nextBtn.onClick.AddListener(GoToPage2);
        nextRT.gameObject.AddComponent<UIButtonHover>();
    }

    private void BuildPage2()
    {
        page2 = new GameObject("Page2");
        page2.transform.SetParent(canvasGO.transform, false);
        var p2rt = page2.AddComponent<RectTransform>();
        p2rt.anchorMin = Vector2.zero; p2rt.anchorMax = Vector2.one;
        p2rt.offsetMin = Vector2.zero; p2rt.offsetMax = Vector2.zero;

        // Scroll background — sized as percentage of screen via anchors
        float halfW = scrollWidthPct / 2f;
        float halfH = scrollHeightPct / 2f;
        var scrollRT = MakeRT("Scroll", page2.transform,
            new Vector2(0.5f - halfW, 0.5f - halfH),
            new Vector2(0.5f + halfW, 0.5f + halfH),
            new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        scrollRT.offsetMin = Vector2.zero;
        scrollRT.offsetMax = Vector2.zero;
        var scrollImg = scrollRT.gameObject.AddComponent<Image>();
        scrollImg.sprite = scrollBg;
        scrollImg.type = Image.Type.Sliced;
        _page2ScrollRT = scrollRT;

        // ── Vertically centered content container (anchor-based) ──
        var contentRT = MakeRT("Content", scrollRT,
            new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.88f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;

        float yOffset = 0f;

        // "~ New Spell ~" header (hardcoded, ~15% above original 48)
        MakeTMP("NewSpellHeader", contentRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(500f, 60f),
            "~ New Spell ~", 50f, Color.black, TextAlignmentOptions.Center);
        yOffset -= 65f;

        // Spell card
        var cardRT = MakeRT("SpellCard", contentRT,
            new Vector2(0.05f, 1f), new Vector2(0.95f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(0f, 460f));
        cardRT.offsetMin = new Vector2(cardRT.offsetMin.x, cardRT.offsetMax.y - 460f);
        var cardImg = cardRT.gameObject.AddComponent<Image>();
        cardImg.sprite = boxSprite;
        cardImg.type = Image.Type.Sliced;
        cardImg.color = new Color(1f, 0.99f, 0.92f);

        // Spell icon at the top of the card (~15% above original 64)
        var iconRT = MakeRT("SpellIcon", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -12f), new Vector2(72f, 72f));
        spellIconImage = iconRT.gameObject.AddComponent<Image>();
        spellIconImage.preserveAspect = true;
        spellIconImage.raycastTarget = false;
        spellIconImage.enabled = false;

        // Loading indicator — shown while icon is generating
        brewingText = MakeTMP("BrewingText", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -58f), new Vector2(300f, 40f),
            "Brewing spell...", 36f, new Color(0.55f, 0.4f, 0.7f), TextAlignmentOptions.Center);
        brewingText.fontStyle = FontStyles.Italic;
        brewingText.gameObject.SetActive(false);

        spellNameText = MakeTMP("SpellName", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -100f), new Vector2(450f, 55f),
            "", 52f, new Color(0.35f, 0.1f, 0.55f), TextAlignmentOptions.Center);

        spellFlavorText = MakeTMP("SpellFlavor", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -160f), new Vector2(430f, 80f),
            "", 28f, new Color(0.5f, 0.5f, 0.5f), TextAlignmentOptions.Center);
        spellFlavorText.fontStyle = FontStyles.Italic;
        spellFlavorText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        spellTagsText = MakeTMP("SpellTags", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -255f), new Vector2(430f, 50f),
            "", 42f, new Color(0.1f, 0.44f, 0.76f), TextAlignmentOptions.Center);

        spellStatsText = MakeTMP("SpellStats", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -315f), new Vector2(430f, 50f),
            "", 42f, new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);

        yOffset -= 460f;

        // Corruption section
        corruptionSection = MakeRT("CorruptionSection", contentRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(500f, 160f));

        MakeTMP("CorruptHeader", corruptionSection,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 0f), new Vector2(500f, 50f),
            "~ Corrupted Spells ~", 40f, new Color(0.79f, 0.17f, 0.17f), TextAlignmentOptions.Center);

        corruptionText = MakeTMP("CorruptBody", corruptionSection,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -55f), new Vector2(450f, 100f),
            "", bodyFontSize, new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);
        corruptionText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        yOffset -= 180f;

        // HP delta — anchored above the begin button
        MakeTMP("StatsHeader", scrollRT,
            new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(500f, 50f),
            "~ Your Stats ~", 40f, Color.white, TextAlignmentOptions.Center);

        hpDeltaText = MakeTMP("HPDelta", scrollRT,
            new Vector2(0.5f, 0.23f), new Vector2(0.5f, 0.23f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(500f, 50f),
            "", 36f, new Color(0.18f, 0.62f, 0.27f), TextAlignmentOptions.Center);

        // Begin button — anchored to bottom of scroll
        var beginRT = MakeRT("BeginBtn", scrollRT,
            new Vector2(0.5f, 0.17f), new Vector2(0.5f, 0.17f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(240f, 70f));
        var beginImg = beginRT.gameObject.AddComponent<Image>();
        beginImg.sprite = buttonSprite;
        beginImg.type = Image.Type.Sliced;
        beginImg.color = new Color(0.72f, 0.58f, 0.42f);
        var beginBtn = beginRT.gameObject.AddComponent<Button>();
        beginBtn.targetGraphic = beginImg;
        beginBtn.onClick.AddListener(Hide);
        beginRT.gameObject.AddComponent<UIButtonHover>();

        MakeTMP("BeginText", beginRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "BEGIN", 48f, Color.white, TextAlignmentOptions.Center);
    }

    // ── Populate ─────────────────────────────────────────────────────────────

    private void Populate(FloorManifestDTO manifest, float prevHp, float newHp)
    {
        // Page 1
        titleText.text = manifest.floor_name ?? "Unknown Floor";
        messageText.text = !string.IsNullOrEmpty(manifest.stage_message)
            ? $"\"{manifest.stage_message}\""
            : "";

        // Page 2 — new spell
        if (manifest.new_spell != null)
        {
            // Show spell icon if available (look up the just-added SpellData in Grimoire)
            SpellData liveSpell = null;
            if (Grimoire.Instance != null)
            {
                foreach (var s in Grimoire.Instance.AllSpells)
                    if (s.spellName == manifest.new_spell.name) { liveSpell = s; break; }
            }
            if (liveSpell != null && liveSpell.icon != null)
            {
                spellIconImage.sprite = liveSpell.icon;
                spellIconImage.enabled = true;
            }
            else
            {
                spellIconImage.enabled = false;
            }

            spellNameText.text = manifest.new_spell.name ?? "";
            spellFlavorText.text = !string.IsNullOrEmpty(manifest.new_spell.flavor)
                ? $"\"{manifest.new_spell.flavor}\""
                : "";

            string tags = manifest.new_spell.tags != null
                ? string.Join("  ", manifest.new_spell.tags)
                : "";
            spellTagsText.text = tags;

            spellStatsText.text = $"DMG: {manifest.new_spell.damage:F0}   " +
                                  $"SPD: {manifest.new_spell.speed:F1}   " +
                                  $"CD: {manifest.new_spell.cooldown:F1}";
        }

        // Corruption section — only shown if the new spell itself is cursed
        corruptionSection.gameObject.SetActive(false);

        // HP delta
        hpDeltaText.text = $"HP: {Mathf.CeilToInt(prevHp)} → {Mathf.CeilToInt(newHp)}";
    }

    // ── Pending spell icon ────────────────────────────────────────────────────

    /// <summary>Call before FadeToBlackThenShow so the scroll knows which spell to watch.</summary>
    public void SetPendingSpellData(SpellData spell)
    {
        pendingSpellData = spell;
    }

    private void RefreshSpellIcon()
    {
        if (spellIconImage == null || brewingText == null) return;

        if (pendingSpellData?.icon != null)
        {
            spellIconImage.sprite  = pendingSpellData.icon;
            spellIconImage.enabled = true;
            brewingText.gameObject.SetActive(false);
            if (brewingCoroutine != null) { StopCoroutine(brewingCoroutine); brewingCoroutine = null; }
        }
        else
        {
            spellIconImage.enabled = false;
            // Only show the brewing indicator when the player is actually on page 2
            bool onPage2 = page2 != null && page2.activeSelf;
            brewingText.gameObject.SetActive(onPage2 && pendingSpellData != null);
            if (onPage2 && pendingSpellData != null && brewingCoroutine == null)
                brewingCoroutine = StartCoroutine(AnimateBrewing());
        }
    }

    private IEnumerator AnimateBrewing()
    {
        string[] states = { "Brewing spell.", "Brewing spell..", "Brewing spell..." };
        int i = 0;
        while (true)
        {
            if (brewingText != null) brewingText.text = states[i % states.Length];
            i++;
            yield return new WaitForSecondsRealtime(0.45f);
        }
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    private void GoToPage2()
    {
        page1.SetActive(false);
        page2.SetActive(true);
        SFXManager.Instance?.PlayScrollOpen();
        if (Grimoire.Instance != null)
            Grimoire.Instance.OnLoadoutChanged += RefreshSpellIcon;
        RefreshSpellIcon();
        if (_page2ScrollRT != null)
            StartCoroutine(UIPanelAnimator.AnimateIn(_page2ScrollRT));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TMP_Text MakeTMP(string name, RectTransform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size,
        string text, float fontSize, Color color, TextAlignmentOptions align)
    {
        var rt = MakeRT(name, parent, anchorMin, anchorMax, pivot, pos, size);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = align;
        tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Truncate;
        tmp.color = color;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = new Color32(0, 0, 0, 180);
        tmp.raycastTarget = false;
        tmp.text = text;
        return tmp;
    }

    private static RectTransform MakeRT(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size)
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
