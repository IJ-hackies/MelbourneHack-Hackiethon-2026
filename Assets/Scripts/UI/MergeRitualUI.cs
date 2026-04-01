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
    [SerializeField] private float rowH   = 52f;
    [SerializeField] private float rowGap = 6f;

    private GameObject canvasGO;
    private RectTransform _panelRoot;
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
    private RectTransform mergeBtnRT;
    private Image mergeBtnImg;
    private RawImage mergeBtnGlow;
    private Image[] mergeSparkles;
    private float[] mergeSparkleAngles;

    private static Texture2D _mergeGlowTex;

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

    /// <summary>The MergeRitual canvas GO for sorting order manipulation.</summary>
    public GameObject CanvasGO => canvasGO;
    /// <summary>Number of spells currently selected for merge.</summary>
    public int SelectedCount => selected.Count;

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
        if (_panelRoot != null)
            StartCoroutine(UIPanelAnimator.AnimateIn(_panelRoot));
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        StartCoroutine(CloseRoutine());
    }

    private System.Collections.IEnumerator CloseRoutine()
    {
        if (_panelRoot != null)
            yield return StartCoroutine(UIPanelAnimator.AnimateOut(_panelRoot));
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

        // Backdrop — stays as direct child of canvas (does not animate)
        var backdrop = MakeRT("Backdrop", canvasGO.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        backdrop.offsetMin = Vector2.zero; backdrop.offsetMax = Vector2.zero;
        var bdImg = backdrop.gameObject.AddComponent<Image>();
        bdImg.color = new Color(0f, 0f, 0f, 0.75f);

        // Panel root — wraps all visual elements so they animate together
        var panelRoot = MakeRT("PanelRoot", canvasGO.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        panelRoot.offsetMin = Vector2.zero;
        panelRoot.offsetMax = Vector2.zero;
        _panelRoot = panelRoot;

        // Panel background — purple box
        var panelBg = MakeRT("PanelBg", panelRoot,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 10f), new Vector2(panelW + 80f, 920f));
        var panelImg = panelBg.gameObject.AddComponent<Image>();
        panelImg.sprite = boxSprite;
        panelImg.type = Image.Type.Sliced;
        panelImg.color = new Color(0.18f, 0.07f, 0.32f, 0.97f);

        // Title
        MakeTMP("Title", panelRoot,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -115f), new Vector2(600f, 48f),
            "~ Merge Ritual ~", 33f, Color.white, TextAlignmentOptions.Center);

        MakeTMP("Subtitle", panelRoot,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -165f), new Vector2(600f, 24f),
            "Select 2-3 spells of the same tier to fuse. Source spells are consumed.", 15f,
            new Color(0.6f, 0.6f, 0.6f), TextAlignmentOptions.Center);

        // Left: spell list
        float leftX = -panelW / 4f - 20f;
        MakeTMP("AvailLabel", panelRoot,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(leftX, -200f), new Vector2(400f, 28f),
            "Available Spells", 22f, Color.white, TextAlignmentOptions.Center);

        spellListContainer = MakeRT("SpellList", panelRoot,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(leftX, -238f), new Vector2(400f, 420f));

        // Right: preview
        float rightX = panelW / 4f + 20f;
        MakeTMP("PreviewLabel", panelRoot,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightX, -200f), new Vector2(430f, 28f),
            "Merge Preview", 22f, Color.white, TextAlignmentOptions.Center);

        previewCard = MakeRT("PreviewCard", panelRoot,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightX, -238f), new Vector2(430f, 340f));

        float py = -15f;
        previewName = MakeTMP("PName", previewCard,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, py), new Vector2(400f, 34f),
            "", 22f, Color.white, TextAlignmentOptions.Center);
        py -= 36f;

        previewFrom = MakeTMP("PFrom", previewCard,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, py), new Vector2(400f, 22f),
            "", 14f, new Color(0.6f, 0.6f, 0.6f), TextAlignmentOptions.Center);
        py -= 38f;

        previewTags = MakeTMP("PTags", previewCard,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, py), new Vector2(400f, 56f),
            "", 14f, new Color(0.1f, 0.44f, 0.76f), TextAlignmentOptions.Center);
        previewTags.textWrappingMode = TMPro.TextWrappingModes.Normal;
        py -= 60f;

        previewStats = MakeTMP("PStats", previewCard,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, py), new Vector2(400f, 68f),
            "", 14f, Color.white, TextAlignmentOptions.Center);
        previewStats.textWrappingMode = TMPro.TextWrappingModes.Normal;
        py -= 72f;

        previewWarning = MakeTMP("PWarn", previewCard,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, py), new Vector2(400f, 38f),
            "", 14f, new Color(0.79f, 0.17f, 0.17f, 0.8f), TextAlignmentOptions.Center);
        previewWarning.textWrappingMode = TMPro.TextWrappingModes.Normal;

        // Bottom buttons — soft radial glow behind merge button, then button on top
        var glowRT = MakeRT("MergeBtnGlow", panelRoot,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
            new Vector2(-110f, 260f), new Vector2(340f, 160f));
        mergeBtnGlow = glowRT.gameObject.AddComponent<RawImage>();
        mergeBtnGlow.texture = GetMergeGlowTexture();
        mergeBtnGlow.color = new Color(0.7f, 0.1f, 1f, 0f);
        mergeBtnGlow.raycastTarget = false;

        mergeBtnRT = MakeRT("MergeBtn", panelRoot,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-110f, 225f), new Vector2(220f, 70f));
        mergeBtnImg = mergeBtnRT.gameObject.AddComponent<Image>();
        mergeBtnImg.sprite = buttonSprite;
        mergeBtnImg.type = Image.Type.Sliced;
        mergeBtnImg.color = new Color(0.44f, 0.28f, 0.63f);
        mergeBtn = mergeBtnRT.gameObject.AddComponent<Button>();
        mergeBtn.targetGraphic = mergeBtnImg;
        mergeBtn.onClick.AddListener(OnMergeClick);
        mergeBtnRT.gameObject.AddComponent<UIButtonHover>();

        mergeBtnText = MakeTMP("MergeTxt", mergeBtnRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "Perform Ritual", 20f, Color.white, TextAlignmentOptions.Center);

        // Sparkle dots that orbit the merge button when active
        mergeSparkles = new Image[6];
        mergeSparkleAngles = new float[] { 0f, 60f, 120f, 180f, 240f, 300f };
        for (int i = 0; i < 6; i++)
        {
            var sparkleRT = MakeRT($"Sparkle_{i}", panelRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-110f, 225f), new Vector2(10f, 10f));
            mergeSparkles[i] = sparkleRT.gameObject.AddComponent<Image>();
            mergeSparkles[i].color = new Color(0.9f, 0.5f, 1f, 0f);
            mergeSparkles[i].raycastTarget = false;
        }

        var skipBtnRT = MakeRT("SkipBtn", panelRoot,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(145f, 225f), new Vector2(130f, 70f));
        var sImg = skipBtnRT.gameObject.AddComponent<Image>();
        sImg.sprite = buttonSprite;
        sImg.type = Image.Type.Sliced;
        sImg.color = new Color(0.72f, 0.58f, 0.42f);
        var skipBtn = skipBtnRT.gameObject.AddComponent<Button>();
        skipBtn.targetGraphic = sImg;
        skipBtn.onClick.AddListener(Close);
        skipBtnRT.gameObject.AddComponent<UIButtonHover>();

        MakeTMP("SkipTxt", skipBtnRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "Skip", 20f, Color.white, TextAlignmentOptions.Center);

        // X close button — top-right of panel
        float closeBtnSize = 44f;
        var closeBtnRT = MakeRT("CloseBtn", panelRoot,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-50f, -100f), new Vector2(closeBtnSize, closeBtnSize));
        var closeBtnImg = closeBtnRT.gameObject.AddComponent<Image>();
        closeBtnImg.sprite = boxSprite;
        closeBtnImg.type = Image.Type.Sliced;
        closeBtnImg.color = new Color(0.55f, 0.12f, 0.08f, 0.9f);
        var closeBtn = closeBtnRT.gameObject.AddComponent<Button>();
        closeBtn.targetGraphic = closeBtnImg;
        closeBtn.onClick.AddListener(Close);
        closeBtnRT.gameObject.AddComponent<UIButtonHover>();
        MakeTMP("CloseBtnLabel", closeBtnRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "X", 22f, Color.white, TextAlignmentOptions.Center);

        canvasGO.SetActive(false);
    }

    private void Update()
    {
        if (!isOpen || mergeBtn == null) return;

        bool active = mergeBtn.interactable;

        // Radial glow — dramatic pulse when active
        if (mergeBtnGlow != null)
        {
            float a = active ? (Mathf.Sin(Time.unscaledTime * 3.5f) * 0.35f + 0.55f) : 0f;
            mergeBtnGlow.color = new Color(0.7f, 0.1f, 1f, a);
        }

        // Button scale pulse when active
        if (mergeBtnRT != null)
        {
            float pulse = active ? (1f + Mathf.Sin(Time.unscaledTime * 3f) * 0.03f) : 1f;
            mergeBtnRT.localScale = new Vector3(pulse, pulse, 1f);
        }

        // Sparkle dots orbiting the button elliptically
        if (mergeSparkles != null)
        {
            for (int i = 0; i < mergeSparkles.Length; i++)
            {
                if (mergeSparkles[i] == null) continue;
                if (active)
                {
                    mergeSparkleAngles[i] += 55f * Time.unscaledDeltaTime;
                    float rad = mergeSparkleAngles[i] * Mathf.Deg2Rad;
                    float x = -110f + Mathf.Cos(rad) * 116f;
                    float y = 260f + Mathf.Sin(rad) * 38f;
                    mergeSparkles[i].rectTransform.anchoredPosition = new Vector2(x, y);
                    float alpha = Mathf.Sin(Time.unscaledTime * 4f + i * 1.05f) * 0.3f + 0.7f;
                    mergeSparkles[i].color = new Color(0.9f, 0.5f, 1f, alpha);
                }
                else
                {
                    mergeSparkles[i].color = new Color(0.9f, 0.5f, 1f, 0f);
                }
            }
        }
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
            // Greyed out if tier doesn't match current selection
            bool tierMismatch = canMerge && selected.Count > 0 && !selected.Contains(spell) && spell.tier != selected[0].tier;

            var row = new MergeRowUI { spell = spell, isSelectable = canMerge };
            row.rt = MakeRT($"Row_{i}", spellListContainer,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(spellListContainer.sizeDelta.x - 10f, rowH));

            row.bg = row.rt.gameObject.AddComponent<Image>();
            row.bg.sprite = boxSprite;
            row.bg.type = Image.Type.Sliced;

            bool isEffectivelySelectable = canMerge && !tierMismatch;

            if (!canMerge || tierMismatch)
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
                isEffectivelySelectable ? (selected.Contains(spell) ? "[x]" : "[ ]") : "",
                13f, isEffectivelySelectable ? new Color(0.18f, 0.62f, 0.27f) : new Color(0.5f, 0.5f, 0.5f),
                TextAlignmentOptions.MidlineLeft);

            // Tier badge — colored pill on the right edge of the row
            Color tierBadgeCol = spell.tier switch
            {
                SpellTier.Basic    => new Color(0.10f, 0.50f, 0.18f, 0.92f),
                SpellTier.Skill    => new Color(0.15f, 0.32f, 0.72f, 0.92f),
                SpellTier.Ultimate => new Color(0.58f, 0.40f, 0.05f, 0.92f),
                _                  => new Color(0.3f, 0.3f, 0.3f, 0.92f),
            };
            string tierInitial = spell.tier == SpellTier.Ultimate ? "ULT" : spell.tier.ToString()[..1];
            const float badgeW = 32f;
            var badgeRT = MakeRT($"TierBadge_{i}", row.rt,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-4f, 0f), new Vector2(badgeW, rowH - 10f));
            var badgeImg = badgeRT.gameObject.AddComponent<Image>();
            badgeImg.color = tierBadgeCol;
            badgeImg.raycastTarget = false;
            if (boxSprite != null) { badgeImg.sprite = boxSprite; badgeImg.type = Image.Type.Sliced; }
            MakeTMP($"TierBadgeTxt_{i}", badgeRT,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero,
                tierInitial, 10f, Color.white, TextAlignmentOptions.Center);

            string label = spell.spellName;
            if (spell.isMerged) label += "  (merged)";

            string tagStr = "";
            if (spell.tags != null)
                foreach (var t in spell.tags) tagStr += t + "  ";

            row.nameText = MakeTMP($"Name_{i}", row.rt,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(35f, 0f), new Vector2(0f, rowH),
                $"{label}\n<size=10>{tagStr.TrimEnd()}</size>",
                13f, isEffectivelySelectable ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.5f, 0.5f, 0.5f),
                TextAlignmentOptions.MidlineLeft);
            row.nameText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            row.nameText.richText = true;
            row.nameText.rectTransform.offsetMax = new Vector2(-(badgeW + 8f), 0f);

            if (isEffectivelySelectable)
            {
                var btn = row.rt.gameObject.AddComponent<Button>();
                btn.targetGraphic = row.bg;
                SpellData captured = spell;
                btn.onClick.AddListener(() => ToggleSelection(captured));
                row.rt.gameObject.AddComponent<UIButtonHover>();
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
            if (selected.Count > 0 && spell.tier != selected[0].tier)
            {
                previewWarning.text = $"Can only merge {selected[0].tier} spells together!";
                return;
            }
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

        if (mergeBtnImg != null)
            mergeBtnImg.color = valid
                ? new Color(0.58f, 0.18f, 0.88f)
                : new Color(0.44f, 0.28f, 0.63f);

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
        float totalDmg = 0f, totalCd = 0f, totalSpd = 0f;
        foreach (var s in selected)
        {
            if (s.tags != null)
                foreach (var t in s.tags) tagSet.Add(t);
            totalDmg += s.damage;
            totalCd += s.cooldown;
            totalSpd += s.speed;
        }

        string tags = "";
        foreach (var t in tagSet) tags += t + "  ";
        previewTags.text = tags.TrimEnd();

        float avgDmg = totalDmg / selected.Count;
        float dmgMult = selected.Count >= 3 ? 1.6f : 1.3f;
        float finalDmg = avgDmg * dmgMult;
        float avgSpd = totalSpd / selected.Count;
        float avgCd = totalCd / selected.Count;
        previewStats.text = $"DMG: {finalDmg:F0}  (avg x {dmgMult:F1})\n" +
                            $"SPD: {avgSpd:F1}  (average)\n" +
                            $"CD: {avgCd:F1}  (average)\n" +
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

    private static Texture2D GetMergeGlowTexture()
    {
        if (_mergeGlowTex != null) return _mergeGlowTex;
        const int size = 64;
        _mergeGlowTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dist = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
            float a = Mathf.Clamp01(1f - dist);
            a = a * a; // quadratic falloff — bright centre, soft transparent edge
            _mergeGlowTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        _mergeGlowTex.Apply();
        return _mergeGlowTex;
    }
}
