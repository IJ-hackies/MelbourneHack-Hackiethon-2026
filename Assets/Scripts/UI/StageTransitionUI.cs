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
    [SerializeField] private float scrollW = 800f;
    [SerializeField] private float scrollH = 1000f;
    [SerializeField] private float titleFontSize = 28f;
    [SerializeField] private float messageFontSize = 20f;
    [SerializeField] private float spellNameFontSize = 24f;
    [SerializeField] private float bodyFontSize = 15f;

    [Header("Transition Timing")]
    [SerializeField] private float fadeToBlackDuration = 0.7f;
    [SerializeField] private float messageFadeInDuration = 1.2f;
    [SerializeField] private float messageFadeInDelay = 0.4f;

    private GameObject canvasGO;
    private GameObject page1;
    private GameObject page2;
    private Action onComplete;

    // Page 1 elements
    private TMP_Text titleText;
    private TMP_Text messageText;
    private TMP_Text attributionText;

    // Page 2 elements
    private TMP_Text spellNameText;
    private TMP_Text spellFlavorText;
    private TMP_Text spellTagsText;
    private TMP_Text spellStatsText;
    private TMP_Text corruptionText;
    private TMP_Text hpDeltaText;
    private RectTransform corruptionSection;

    // Fade overlay
    private Image fadeOverlay;
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
        Populate(manifest, prevHp, newHp);
    }

    public void Hide()
    {
        if (canvasGO != null) canvasGO.SetActive(false);
        if (fadeOverlay != null) fadeOverlay.color = new Color(0f, 0f, 0f, 0f);
        PauseManager.Unpause();
        onComplete?.Invoke();
        onComplete = null;
    }

    private IEnumerator FadeToBlackSequence()
    {
        // Phase 1: Fade the screen to black (game still running)
        fadeOverlay.color = new Color(0f, 0f, 0f, 0f);
        fadeOverlay.raycastTarget = true; // block input during fade
        float elapsed = 0f;
        while (elapsed < fadeToBlackDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeToBlackDuration);
            // Ease-in curve for a smooth darkening
            float alpha = t * t;
            fadeOverlay.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }
        fadeOverlay.color = Color.black;

        // Phase 2: Screen is fully black — pause and show scroll
        PauseManager.Pause();
        page1.SetActive(true);
        page2.SetActive(false);

        // Hide text initially for fade-in
        SetTextAlpha(titleText, 0f);
        SetTextAlpha(messageText, 0f);
        SetTextAlpha(attributionText, 0f);

        // Small delay before text appears
        yield return new WaitForSecondsRealtime(messageFadeInDelay);

        // Phase 3: Fade in the chronicle text
        elapsed = 0f;
        while (elapsed < messageFadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / messageFadeInDuration);
            // Ease-out curve
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

        // Scroll background
        var scrollRT = MakeRT("Scroll", page1.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(scrollW, scrollH));
        var scrollImg = scrollRT.gameObject.AddComponent<Image>();
        scrollImg.sprite = scrollBg;
        scrollImg.type = Image.Type.Sliced;

        // ── Vertically centered content container ──
        // Anchored to center of scroll, holds title + divider + message
        var contentRT = MakeRT("Content", scrollRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 20f), new Vector2(scrollW - 120f, 400f));

        // Title — top of content block
        titleText = MakeTMP("Title", contentRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 0f), new Vector2(scrollW - 120f, 70f),
            "", titleFontSize, Color.white, TextAlignmentOptions.Center);

        // Divider
        var divRT = MakeRT("Divider", contentRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -75f), new Vector2(scrollW - 200f, 2f));
        var divImg = divRT.gameObject.AddComponent<Image>();
        divImg.color = new Color(0.58f, 0.42f, 0.18f, 0.5f);

        // Chronicle message — vertically centered text
        messageText = MakeTMP("Message", contentRT,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -50f), Vector2.zero,
            "", messageFontSize, new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);
        messageText.fontStyle = FontStyles.Italic;
        messageText.enableWordWrapping = true;
        // Stretch to fill remaining content area below divider
        var msgRT = messageText.GetComponent<RectTransform>();
        msgRT.anchorMin = new Vector2(0.05f, 0f);
        msgRT.anchorMax = new Vector2(0.95f, 0.78f);
        msgRT.offsetMin = Vector2.zero;
        msgRT.offsetMax = Vector2.zero;
        msgRT.anchoredPosition = Vector2.zero;

        // Attribution — near bottom of scroll
        attributionText = MakeTMP("Attribution", scrollRT,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 150f), new Vector2(scrollW - 100f, 30f),
            "— The Chronicle", 16f, new Color(0.53f, 0.18f, 0.61f, 0.7f), TextAlignmentOptions.Center);

        // Next page button
        var nextRT = MakeRT("NextBtn", scrollRT,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-55f, 100f), new Vector2(60f, 60f));
        var nextImg = nextRT.gameObject.AddComponent<Image>();
        nextImg.sprite = nextPageArrow;
        nextImg.preserveAspect = true;
        var nextBtn = nextRT.gameObject.AddComponent<Button>();
        nextBtn.targetGraphic = nextImg;
        nextBtn.onClick.AddListener(GoToPage2);
    }

    private void BuildPage2()
    {
        page2 = new GameObject("Page2");
        page2.transform.SetParent(canvasGO.transform, false);
        var p2rt = page2.AddComponent<RectTransform>();
        p2rt.anchorMin = Vector2.zero; p2rt.anchorMax = Vector2.one;
        p2rt.offsetMin = Vector2.zero; p2rt.offsetMax = Vector2.zero;

        // Scroll background
        var scrollRT = MakeRT("Scroll", page2.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(scrollW, scrollH));
        var scrollImg = scrollRT.gameObject.AddComponent<Image>();
        scrollImg.sprite = scrollBg;
        scrollImg.type = Image.Type.Sliced;

        // ── Vertically centered content container for page 2 ──
        // Estimate total content height: header(40) + card(220) + corruption(130) + stats(65) + spacing = ~520
        var contentRT = MakeRT("Content", scrollRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 20f), new Vector2(scrollW - 80f, 520f));

        float yOffset = 0f;

        // "~ New Spell ~" header
        MakeTMP("NewSpellHeader", contentRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(scrollW - 80f, 40f),
            "~ New Spell ~", 24f, Color.white, TextAlignmentOptions.Center);
        yOffset -= 50f;

        // Spell card
        var cardRT = MakeRT("SpellCard", contentRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(scrollW - 100f, 220f));
        var cardImg = cardRT.gameObject.AddComponent<Image>();
        cardImg.sprite = boxSprite;
        cardImg.type = Image.Type.Sliced;
        cardImg.color = new Color(1f, 0.99f, 0.92f);

        spellNameText = MakeTMP("SpellName", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -15f), new Vector2(scrollW - 140f, 35f),
            "", spellNameFontSize, Color.white, TextAlignmentOptions.Center);

        spellFlavorText = MakeTMP("SpellFlavor", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -55f), new Vector2(scrollW - 140f, 55f),
            "", bodyFontSize, new Color(0.5f, 0.5f, 0.5f), TextAlignmentOptions.Center);
        spellFlavorText.fontStyle = FontStyles.Italic;
        spellFlavorText.enableWordWrapping = true;

        spellTagsText = MakeTMP("SpellTags", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -120f), new Vector2(scrollW - 140f, 30f),
            "", bodyFontSize, new Color(0.1f, 0.44f, 0.76f), TextAlignmentOptions.Center);

        spellStatsText = MakeTMP("SpellStats", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -160f), new Vector2(scrollW - 140f, 30f),
            "", bodyFontSize, new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);

        yOffset -= 240f;

        // Corruption section
        corruptionSection = MakeRT("CorruptionSection", contentRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(scrollW - 80f, 130f));

        MakeTMP("CorruptHeader", corruptionSection,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 0f), new Vector2(scrollW - 80f, 30f),
            "~ Corrupted Spells ~", 20f, new Color(0.79f, 0.17f, 0.17f), TextAlignmentOptions.Center);

        corruptionText = MakeTMP("CorruptBody", corruptionSection,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -35f), new Vector2(scrollW - 100f, 85f),
            "", bodyFontSize, new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);
        corruptionText.enableWordWrapping = true;

        yOffset -= 145f;

        // HP delta
        MakeTMP("StatsHeader", contentRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(scrollW - 80f, 30f),
            "~ Your Stats ~", 20f, Color.white, TextAlignmentOptions.Center);
        yOffset -= 35f;

        hpDeltaText = MakeTMP("HPDelta", contentRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(scrollW - 80f, 35f),
            "", 18f, new Color(0.18f, 0.62f, 0.27f), TextAlignmentOptions.Center);

        // Begin button — anchored to bottom of scroll
        var beginRT = MakeRT("BeginBtn", scrollRT,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 100f), new Vector2(180f, 58f));
        var beginImg = beginRT.gameObject.AddComponent<Image>();
        beginImg.sprite = buttonSprite;
        beginImg.type = Image.Type.Sliced;
        beginImg.color = new Color(0.72f, 0.58f, 0.42f);
        var beginBtn = beginRT.gameObject.AddComponent<Button>();
        beginBtn.targetGraphic = beginImg;
        beginBtn.onClick.AddListener(Hide);

        MakeTMP("BeginText", beginRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "BEGIN", 24f, Color.white, TextAlignmentOptions.Center);
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

        // Corruptions
        if (manifest.corrupted_spells != null && manifest.corrupted_spells.Length > 0)
        {
            corruptionSection.gameObject.SetActive(true);
            string corruptText = "";
            foreach (var c in manifest.corrupted_spells)
            {
                corruptText += c.spell_name + "\n";
                if (c.added_tags != null)
                    foreach (var t in c.added_tags) corruptText += $"  + {t}\n";
                if (c.removed_tags != null)
                    foreach (var t in c.removed_tags) corruptText += $"  - {t}\n";
            }
            corruptionText.text = corruptText.TrimEnd();
        }
        else
        {
            corruptionSection.gameObject.SetActive(false);
        }

        // HP delta
        hpDeltaText.text = $"HP: {Mathf.CeilToInt(prevHp)} → {Mathf.CeilToInt(newHp)}";
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    private void GoToPage2()
    {
        page1.SetActive(false);
        page2.SetActive(true);
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
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
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
