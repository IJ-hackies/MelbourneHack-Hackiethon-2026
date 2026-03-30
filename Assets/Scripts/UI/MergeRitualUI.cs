using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full-screen merge ritual overlay. Player selects 2-3 spells to fuse.
/// Shows live preview of combined tags/stats. Already-merged spells are greyed out.
///
/// Setup: Create empty GameObject, attach this script, assign sprites + font.
/// Toggle via HudIconBar or call Toggle() directly.
/// </summary>
public class MergeRitualUI : MonoBehaviour
{
    [Header("Cutscene")]
    [SerializeField] private MergeCutscene mergeCutscene;

    [Header("Sprites")]
    [SerializeField] private Sprite boxSprite;     // box.png
    [SerializeField] private Sprite buttonSprite;  // button.png

    [Header("Font")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Layout (tweak in Inspector)")]
    [SerializeField] private float panelW = 900f;
#pragma warning disable CS0414
    [SerializeField] private float panelH = 560f;
#pragma warning restore CS0414
    [SerializeField] private float rowH   = 48f;
    [SerializeField] private float rowGap = 6f;

    private GameObject canvasGO;
    private bool isOpen;

    private RectTransform spellListContainer;
    private readonly List<MergeRowUI> rows = new();
    private readonly List<SpellData> selected = new();

    // Preview
    private TMP_Text previewName;
    private TMP_Text previewFrom;
    private TMP_Text previewTags;
    private TMP_Text previewStats;
    private TMP_Text previewWarning;
    private RectTransform previewCard;

    // Buttons
    private Button mergeBtn;
    private TMP_Text mergeBtnText;

    private class MergeRowUI
    {
        public RectTransform rt;
        public Image bg;
        public TMP_Text nameText;
        public TMP_Text checkText;
        public SpellData spell;
        public bool isSelectable;
    }

    public bool IsOpen => isOpen;

    public void Toggle()
    {
        if (isOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (canvasGO == null) Build();
        selected.Clear();
        isOpen = true;
        canvasGO.SetActive(true);
        PauseManager.Pause();
        RefreshList();
        UpdatePreview();
    }

    public void Close()
    {
        isOpen = false;
        if (canvasGO != null) canvasGO.SetActive(false);
        PauseManager.Unpause();
    }

    private void OnDestroy()
    {
        if (canvasGO != null) Destroy(canvasGO);
    }

    // ── Build ────────────────────────────────────────────────────────────────

    private void Build()
    {
        canvasGO = new GameObject("MergeRitualUI_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 220;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Backdrop
        var backdrop = MakeRT("Backdrop", canvasGO.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        backdrop.offsetMin = Vector2.zero; backdrop.offsetMax = Vector2.zero;
        var bdImg = backdrop.gameObject.AddComponent<Image>();
        bdImg.color = new Color(0f, 0f, 0f, 0.75f);

        // Panel background — black rounded-edge box
        var panelBg = MakeRT("PanelBg", canvasGO.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(panelW + 80f, 600f));
        var panelImg = panelBg.gameObject.AddComponent<Image>();
        panelImg.sprite = boxSprite;
        panelImg.type = Image.Type.Sliced;
        panelImg.color = new Color(0.06f, 0.06f, 0.08f, 0.94f);

        // Title
        MakeTMP("Title", canvasGO.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -40f), new Vector2(500f, 40f),
            "~ Merge Ritual ~", 28f, Color.white, TextAlignmentOptions.Center);

        MakeTMP("Subtitle", canvasGO.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -80f), new Vector2(500f, 20f),
            "Select 2-3 spells to fuse. Source spells are consumed.", 13f,
            new Color(0.6f, 0.6f, 0.6f), TextAlignmentOptions.Center);

        // Left: spell list
        float leftX = -panelW / 4f - 20f;
        MakeTMP("AvailLabel", canvasGO.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(leftX, -110f), new Vector2(340f, 25f),
            "Available Spells", 18f, Color.white, TextAlignmentOptions.Center);

        spellListContainer = MakeRT("SpellList", canvasGO.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(leftX, -145f), new Vector2(340f, 400f));

        // Right: preview
        float rightX = panelW / 4f + 20f;
        MakeTMP("PreviewLabel", canvasGO.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightX, -110f), new Vector2(380f, 25f),
            "Merge Preview", 18f, Color.white, TextAlignmentOptions.Center);

        previewCard = MakeRT("PreviewCard", canvasGO.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightX, -145f), new Vector2(380f, 300f));
        var pcImg = previewCard.gameObject.AddComponent<Image>();
        pcImg.color = new Color(0.55f, 0.35f, 0.65f, 0.3f);

        float py = -15f;
        previewName = MakeTMP("PName", previewCard,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, py), new Vector2(350f, 30f),
            "", 20f, Color.white, TextAlignmentOptions.Center);
        py -= 30f;

        previewFrom = MakeTMP("PFrom", previewCard,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, py), new Vector2(350f, 20f),
            "", 12f, new Color(0.6f, 0.6f, 0.6f), TextAlignmentOptions.Center);
        py -= 35f;

        previewTags = MakeTMP("PTags", previewCard,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, py), new Vector2(350f, 50f),
            "", 12f, new Color(0.1f, 0.44f, 0.76f), TextAlignmentOptions.Center);
        previewTags.textWrappingMode = TMPro.TextWrappingModes.Normal;
        py -= 55f;

        previewStats = MakeTMP("PStats", previewCard,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, py), new Vector2(350f, 60f),
            "", 12f, Color.white, TextAlignmentOptions.Center);
        previewStats.textWrappingMode = TMPro.TextWrappingModes.Normal;
        py -= 65f;

        previewWarning = MakeTMP("PWarn", previewCard,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, py), new Vector2(350f, 35f),
            "", 12f, new Color(0.79f, 0.17f, 0.17f, 0.8f), TextAlignmentOptions.Center);
        previewWarning.textWrappingMode = TMPro.TextWrappingModes.Normal;

        // Bottom buttons
        var mergeBtnRT = MakeRT("MergeBtn", canvasGO.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-60f, 60f), new Vector2(160f, 50f));
        var mImg = mergeBtnRT.gameObject.AddComponent<Image>();
        mImg.sprite = buttonSprite;
        mImg.type = Image.Type.Sliced;
        mImg.color = new Color(0.44f, 0.28f, 0.63f);
        mergeBtn = mergeBtnRT.gameObject.AddComponent<Button>();
        mergeBtn.targetGraphic = mImg;
        mergeBtn.onClick.AddListener(OnMergeClick);

        mergeBtnText = MakeTMP("MergeTxt", mergeBtnRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "Perform Ritual", 18f, Color.white, TextAlignmentOptions.Center);

        var skipBtnRT = MakeRT("SkipBtn", canvasGO.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(100f, 60f), new Vector2(110f, 50f));
        var sImg = skipBtnRT.gameObject.AddComponent<Image>();
        sImg.sprite = buttonSprite;
        sImg.type = Image.Type.Sliced;
        sImg.color = new Color(0.72f, 0.58f, 0.42f);
        var skipBtn = skipBtnRT.gameObject.AddComponent<Button>();
        skipBtn.targetGraphic = sImg;
        skipBtn.onClick.AddListener(Close);

        MakeTMP("SkipTxt", skipBtnRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "Skip", 18f, Color.white, TextAlignmentOptions.Center);

        canvasGO.SetActive(false);
    }

    // ── Refresh list ─────────────────────────────────────────────────────────

    private void RefreshList()
    {
        foreach (var row in rows)
            if (row.rt != null) Destroy(row.rt.gameObject);
        rows.Clear();

        var grimoire = Grimoire.Instance;
        if (grimoire == null) return;

        var allSpells = grimoire.AllSpells;
        for (int i = 0; i < allSpells.Count; i++)
        {
            SpellData spell = allSpells[i];
            float y = -(i * (rowH + rowGap));
            bool canMerge = !spell.isMerged;

            var row = new MergeRowUI { spell = spell, isSelectable = canMerge };
            row.rt = MakeRT($"Row_{i}", spellListContainer,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(spellListContainer.sizeDelta.x - 10f, rowH));

            row.bg = row.rt.gameObject.AddComponent<Image>();
            row.bg.sprite = boxSprite;
            row.bg.type = Image.Type.Sliced;

            if (!canMerge)
            {
                row.bg.color = new Color(0.9f, 0.9f, 0.9f, 0.4f);
            }
            else
            {
                row.bg.color = selected.Contains(spell)
                    ? new Color(0.83f, 0.97f, 0.83f)
                    : new Color(1f, 0.99f, 0.92f);
            }

            row.checkText = MakeTMP($"Check_{i}", row.rt,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(8f, 0f), new Vector2(25f, rowH),
                canMerge ? (selected.Contains(spell) ? "[x]" : "[ ]") : "",
                13f, canMerge ? new Color(0.18f, 0.62f, 0.27f) : new Color(0.5f, 0.5f, 0.5f),
                TextAlignmentOptions.MidlineLeft);

            string label = spell.spellName;
            if (spell.isMerged) label += "  (merged)";

            string tagStr = "";
            if (spell.tags != null)
                foreach (var t in spell.tags) tagStr += t + "  ";

            row.nameText = MakeTMP($"Name_{i}", row.rt,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(35f, 0f), new Vector2(0f, rowH),
                $"{label}\n<size=10>{tagStr.TrimEnd()}</size>",
                13f, canMerge ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.5f, 0.5f, 0.5f),
                TextAlignmentOptions.MidlineLeft);
            row.nameText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            row.nameText.richText = true;
            row.nameText.rectTransform.offsetMax = new Vector2(-10f, 0f);

            if (canMerge)
            {
                var btn = row.rt.gameObject.AddComponent<Button>();
                btn.targetGraphic = row.bg;
                SpellData captured = spell;
                btn.onClick.AddListener(() => ToggleSelection(captured));
            }

            rows.Add(row);
        }
    }

    private void ToggleSelection(SpellData spell)
    {
        if (selected.Contains(spell))
        {
            selected.Remove(spell);
        }
        else
        {
            if (selected.Count >= 3) return; // max 3
            selected.Add(spell);
        }
        RefreshList();
        UpdatePreview();
    }

    // ── Preview ──────────────────────────────────────────────────────────────

    private void UpdatePreview()
    {
        bool valid = selected.Count >= 2;
        mergeBtn.interactable = valid;
        mergeBtnText.color = valid ? Color.white : new Color(1f, 1f, 1f, 0.3f);

        if (!valid)
        {
            previewName.text = selected.Count == 0
                ? "Select spells to merge"
                : "Select at least 2 spells";
            previewFrom.text = "";
            previewTags.text = "";
            previewStats.text = "";
            previewWarning.text = "";
            return;
        }

        previewName.text = "??? (name from Gemini)";
        previewFrom.text = "Merged from: " + string.Join(" + ", selected.ConvertAll(s => s.spellName));

        // Combined tags
        var tagSet = new HashSet<SpellTag>();
        float totalDmg = 0f, maxCd = 0f, totalSpd = 0f;
        foreach (var s in selected)
        {
            if (s.tags != null)
                foreach (var t in s.tags) tagSet.Add(t);
            totalDmg += s.damage;
            if (s.cooldown > maxCd) maxCd = s.cooldown;
            totalSpd += s.speed;
        }

        string tags = "";
        foreach (var t in tagSet) tags += t + "  ";
        previewTags.text = tags.TrimEnd();

        float avgSpd = totalSpd / selected.Count;
        float finalDmg = totalDmg * 0.5f;
        float finalCd = maxCd * 2f;
        previewStats.text = $"DMG: {finalDmg:F0}  (total x 0.5)\n" +
                            $"SPD: {avgSpd:F1}  (average)\n" +
                            $"CD: {finalCd:F1}  (max x 2.0)\n" +
                            $"Fires all {selected.Count} projectiles simultaneously";

        // Warning for corruption
        bool hasCorruption = tagSet.Contains(SpellTag.SELF_DAMAGE)
                          || tagSet.Contains(SpellTag.ENEMY_HOMING)
                          || tagSet.Contains(SpellTag.REVERSED_CONTROLS);
        previewWarning.text = hasCorruption
            ? "Warning: inherits corruption tags!"
            : "";
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private void OnMergeClick()
    {
        if (selected.Count < 2) return;

        var grimoire = Grimoire.Instance;
        if (grimoire == null) return;

        // Capture source spells BEFORE merge consumes them (icons are needed for cutscene)
        SpellData[] sources = selected.ToArray();

        string placeholderName   = "Merged: " + string.Join(" & ", selected.ConvertAll(s => s.spellName));
        string placeholderFlavor = "Two powers, one vessel.";

        SpellData merged = grimoire.MergeSpells(sources, placeholderName, placeholderFlavor);
        selected.Clear();

        // Generate icon immediately — procedural generation is instant.
        merged.icon = ProceduralSpellIconGenerator.Generate(merged);
        Grimoire.Instance?.NotifyLoadoutChanged();
        bool iconReady = true;

        // Hide this UI and hand off to the cutscene
        if (canvasGO != null) canvasGO.SetActive(false);

        MergeCutscene cutscene = mergeCutscene != null
            ? mergeCutscene
            : FindAnyObjectByType<MergeCutscene>();

        if (cutscene != null)
        {
            cutscene.Play(sources, merged, () => iconReady, () =>
            {
                if (canvasGO != null) canvasGO.SetActive(true);
                RefreshList();
                UpdatePreview();
            });
        }
        else
        {
            // Fallback: no cutscene in scene — behave as before
            SFXManager.Instance?.PlayMergeSpell();
            if (canvasGO != null) canvasGO.SetActive(true);
            RefreshList();
            UpdatePreview();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color = color;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = new Color32(0, 0, 0, 180);
        tmp.raycastTarget = false;
        tmp.text = text;
        return tmp;
    }

    private TMP_Text MakeTMP(string name, Transform parent,
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
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
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
