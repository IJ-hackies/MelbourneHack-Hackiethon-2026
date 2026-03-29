using System;
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
    [SerializeField] private float scrollW = 420f;
    [SerializeField] private float scrollH = 700f;
    [SerializeField] private float titleFontSize = 22f;
    [SerializeField] private float messageFontSize = 16f;
    [SerializeField] private float spellNameFontSize = 20f;
    [SerializeField] private float bodyFontSize = 13f;

    private GameObject canvasGO;
    private GameObject page1;
    private GameObject page2;
    private Action onComplete;

    // Page 1 elements
    private TMP_Text titleText;
    private TMP_Text messageText;

    // Page 2 elements
    private TMP_Text spellNameText;
    private TMP_Text spellFlavorText;
    private TMP_Text spellTagsText;
    private TMP_Text spellStatsText;
    private TMP_Text corruptionText;
    private TMP_Text hpDeltaText;
    private RectTransform corruptionSection;

    public bool IsOpen => canvasGO != null && canvasGO.activeSelf;

    /// <summary>Show the transition screen. onDone is called when the player clicks Begin.</summary>
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
        PauseManager.Unpause();
        onComplete?.Invoke();
        onComplete = null;
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

        // Dark backdrop
        var backdrop = MakeRT("Backdrop", canvasGO.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        backdrop.offsetMin = Vector2.zero;
        backdrop.offsetMax = Vector2.zero;
        var bdImg = backdrop.gameObject.AddComponent<Image>();
        bdImg.color = new Color(0f, 0f, 0f, 0.7f);

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
        scrollImg.type = Image.Type.Simple;
        scrollImg.preserveAspect = true;

        // Title
        titleText = MakeTMP("Title", scrollRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -80f), new Vector2(scrollW - 100f, 60f),
            "", titleFontSize, Color.white, TextAlignmentOptions.Center);

        // Divider
        var divRT = MakeRT("Divider", scrollRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -145f), new Vector2(scrollW - 140f, 2f));
        var divImg = divRT.gameObject.AddComponent<Image>();
        divImg.color = new Color(0.58f, 0.42f, 0.18f, 0.5f);

        // Chronicle message
        messageText = MakeTMP("Message", scrollRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -170f), new Vector2(scrollW - 120f, 350f),
            "", messageFontSize, new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);
        messageText.fontStyle = FontStyles.Italic;
        messageText.enableWordWrapping = true;

        // Attribution
        MakeTMP("Attribution", scrollRT,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 130f), new Vector2(scrollW - 100f, 25f),
            "— The Chronicle", 14f, new Color(0.53f, 0.18f, 0.61f, 0.7f), TextAlignmentOptions.Center);

        // Next page button
        var nextRT = MakeRT("NextBtn", scrollRT,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-40f, 70f), new Vector2(50f, 50f));
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
        scrollImg.type = Image.Type.Simple;
        scrollImg.preserveAspect = true;

        float yOffset = -80f;

        // "~ New Spell ~" header
        MakeTMP("NewSpellHeader", scrollRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(scrollW - 80f, 30f),
            "~ New Spell ~", 20f, Color.white, TextAlignmentOptions.Center);
        yOffset -= 40f;

        // Spell card
        var cardRT = MakeRT("SpellCard", scrollRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(scrollW - 100f, 180f));
        var cardImg = cardRT.gameObject.AddComponent<Image>();
        cardImg.sprite = boxSprite;
        cardImg.type = Image.Type.Sliced;
        cardImg.color = new Color(1f, 0.99f, 0.92f);

        spellNameText = MakeTMP("SpellName", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -12f), new Vector2(280f, 30f),
            "", 20f, Color.white, TextAlignmentOptions.Center);

        spellFlavorText = MakeTMP("SpellFlavor", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -45f), new Vector2(280f, 45f),
            "", 12f, new Color(0.5f, 0.5f, 0.5f), TextAlignmentOptions.Center);
        spellFlavorText.fontStyle = FontStyles.Italic;
        spellFlavorText.enableWordWrapping = true;

        spellTagsText = MakeTMP("SpellTags", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -100f), new Vector2(280f, 25f),
            "", 12f, new Color(0.1f, 0.44f, 0.76f), TextAlignmentOptions.Center);

        spellStatsText = MakeTMP("SpellStats", cardRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -130f), new Vector2(280f, 25f),
            "", 13f, new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);

        yOffset -= 195f;

        // Corruption section
        corruptionSection = MakeRT("CorruptionSection", scrollRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(scrollW - 80f, 110f));

        MakeTMP("CorruptHeader", corruptionSection,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 0f), new Vector2(scrollW - 80f, 25f),
            "~ Corrupted Spells ~", 16f, new Color(0.79f, 0.17f, 0.17f), TextAlignmentOptions.Center);

        corruptionText = MakeTMP("CorruptBody", corruptionSection,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -30f), new Vector2(scrollW - 100f, 70f),
            "", 13f, new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);
        corruptionText.enableWordWrapping = true;

        yOffset -= 120f;

        // HP delta
        MakeTMP("StatsHeader", scrollRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(scrollW - 80f, 25f),
            "~ Your Stats ~", 16f, Color.white, TextAlignmentOptions.Center);
        yOffset -= 30f;

        hpDeltaText = MakeTMP("HPDelta", scrollRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, yOffset), new Vector2(scrollW - 80f, 30f),
            "", 16f, new Color(0.18f, 0.62f, 0.27f), TextAlignmentOptions.Center);

        // Begin button
        var beginRT = MakeRT("BeginBtn", scrollRT,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 70f), new Vector2(140f, 50f));
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
            "BEGIN", 22f, Color.white, TextAlignmentOptions.Center);
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
