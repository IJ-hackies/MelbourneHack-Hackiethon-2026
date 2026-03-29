using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Grimoire book overlay — left page shows spell library, right page shows
/// selected spell details + equip button. 3 loadout slots shown below the book.
///
/// Setup: Create empty GameObject, attach this script, assign sprites + font.
/// Toggle with Tab/G key (handled by HudIconBar) or call Toggle() directly.
/// </summary>
public class GrimoireUI : MonoBehaviour
{
    [Header("Sprites")]
    [SerializeField] private Sprite bookSprite;         // GrimoireBook.png
    [SerializeField] private Sprite slotSprite;         // SpellSlot.png
    [SerializeField] private Sprite slotSelectedSprite; // SpellSlotSelected.png
    [SerializeField] private Sprite boxSprite;          // box.png
    [SerializeField] private Sprite buttonSprite;       // button.png

    [Header("Font")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Layout (tweak in Inspector)")]
    [SerializeField] private float bookW = 1050f;
    [SerializeField] private float rowH = 42f;
    [SerializeField] private float rowGap = 6f;
    [SerializeField] private float titleFontSize = 18f;
    [SerializeField] private float detailNameFontSize = 22f;
    [SerializeField] private float detailBodyFontSize = 12f;
    [SerializeField] private float statsFontSize = 14f;
    [SerializeField] private float listFontSize = 14f;
    [SerializeField] private float loadoutSlotSize = 65f;

    private GameObject canvasGO;
    private bool isOpen;

    // Left page: library list
    private RectTransform libraryContainer;
    private readonly List<SpellRowUI> spellRows = new();

    // Right page: detail view
    private TMP_Text detailName;
    private TMP_Text detailElement;
    private TMP_Text detailFlavor;
    private TMP_Text detailCorruptionFlavor;
    private TMP_Text detailTags;
    private TMP_Text detailStats;
    private RectTransform equipBtnRT;
    private int equipTargetSlot = 0;
    private TMP_Text equipBtnText;

    // Bottom: loadout slots
    private Image[] loadoutSlotImages;
    private TMP_Text[] loadoutSlotNames;

    private SpellData selectedSpell;

    private class SpellRowUI
    {
        public RectTransform rt;
        public Image bg;
        public TMP_Text nameText;
        public SpellData spell;
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
        isOpen = true;
        canvasGO.SetActive(true);
        PauseManager.Pause();
        RefreshLibrary();
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
        canvasGO = new GameObject("GrimoireUI_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Backdrop
        var backdrop = MakeRT("Backdrop", canvasGO.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        backdrop.offsetMin = Vector2.zero; backdrop.offsetMax = Vector2.zero;
        var bdImg = backdrop.gameObject.AddComponent<Image>();
        bdImg.color = new Color(0f, 0f, 0f, 0.7f);
        var bdBtn = backdrop.gameObject.AddComponent<Button>();
        bdBtn.targetGraphic = bdImg;
        bdBtn.onClick.AddListener(Close);

        // ── Book background ─────────────────────────────────────────────
        // Match rect to sprite aspect so the book fills the rect exactly
        float spriteAspect = bookSprite != null
            ? bookSprite.rect.width / bookSprite.rect.height
            : 1.805f;
        float bH = bookW / spriteAspect;

        var bookRT = MakeRT("Book", canvasGO.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 30f), new Vector2(bookW, bH));
        var bookImg = bookRT.gameObject.AddComponent<Image>();
        bookImg.sprite = bookSprite;

        // ── Page content areas (derived from sprite proportions) ────────
        // GrimoireBook.png (278×154): writable page areas approximately:
        //   Left page:  x 18..127    Right page: x 150..260    y: 10..120
        const float kBorderFrac = 0.065f;  // outer border (18/278)
        const float kSpineLFrac = 0.457f;  // left page right edge (127/278)
        const float kSpineRFrac = 0.540f;  // right page left edge (150/278)
        const float kTopFrac    = 0.065f;  // top border (10/154)
        const float kBotFrac    = 0.780f;  // bottom of writable area (120/154)
        const float kInnerPad   = 10f;     // breathing room per side

        float lpLeft  = bookW * kBorderFrac + kInnerPad;
        float lpRight = bookW * kSpineLFrac - kInnerPad;
        float rpLeft  = bookW * kSpineRFrac + kInnerPad;
        float rpRight = bookW * (1f - kBorderFrac) - kInnerPad;
        float pTop    = bH * kTopFrac + kInnerPad;

        float pageW      = Mathf.Min(lpRight - lpLeft, rpRight - rpLeft);
        float pageH      = bH * kBotFrac - kInnerPad - pTop;
        float leftPageX  = (lpLeft + lpRight) / 2f - bookW / 2f;
        float rightPageX = (rpLeft + rpRight) / 2f - bookW / 2f;
        float topOffset  = pTop;

        // ── Left page: library ──────────────────────────────────────────
        MakeTMP("LibTitle", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(leftPageX, -topOffset), new Vector2(pageW, 30f),
            "~ Spell Library ~", titleFontSize, new Color(0.12f, 0.12f, 0.12f), TextAlignmentOptions.Center);

        libraryContainer = MakeRT("LibList", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(leftPageX, -(topOffset + 40f)), new Vector2(pageW, pageH - 50f));

        // ── Right page: spell details ───────────────────────────────────
        MakeTMP("DetailTitle", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, -topOffset), new Vector2(pageW, 30f),
            "~ Spell Details ~", titleFontSize, new Color(0.12f, 0.12f, 0.12f), TextAlignmentOptions.Center);

        float dy = -(topOffset + 40f);

        detailName = MakeTMP("DName", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 30f),
            "", detailNameFontSize, Color.white, TextAlignmentOptions.Center);
        dy -= 28f;

        detailElement = MakeTMP("DElement", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 20f),
            "", detailBodyFontSize, new Color(0.9f, 0.47f, 0f), TextAlignmentOptions.Center);
        dy -= 25f;

        detailFlavor = MakeTMP("DFlavor", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 50f),
            "", detailBodyFontSize, new Color(0.35f, 0.35f, 0.35f), TextAlignmentOptions.Center);
        detailFlavor.fontStyle = FontStyles.Italic;
        dy -= 50f;

        detailCorruptionFlavor = MakeTMP("DCFlavor", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 30f),
            "", detailBodyFontSize, new Color(0.79f, 0.17f, 0.17f, 0.85f), TextAlignmentOptions.Center);
        detailCorruptionFlavor.fontStyle = FontStyles.Italic;
        dy -= 30f;

        detailTags = MakeTMP("DTags", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 25f),
            "", detailBodyFontSize, new Color(0.1f, 0.44f, 0.76f), TextAlignmentOptions.Center);
        dy -= 30f;

        detailStats = MakeTMP("DStats", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 25f),
            "", statsFontSize, new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);
        dy -= 40f;

        // Equip button
        equipBtnRT = MakeRT("EquipBtn", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(Mathf.Min(170f, pageW - 20f), 40f));
        var eqImg = equipBtnRT.gameObject.AddComponent<Image>();
        eqImg.sprite = buttonSprite;
        eqImg.type = Image.Type.Sliced;
        eqImg.color = new Color(0.72f, 0.58f, 0.42f);
        var eqBtn = equipBtnRT.gameObject.AddComponent<Button>();
        eqBtn.targetGraphic = eqImg;
        eqBtn.onClick.AddListener(OnEquipClick);

        equipBtnText = MakeTMP("EquipText", equipBtnRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "Equip to Slot 1", statsFontSize, Color.white, TextAlignmentOptions.Center);

        // ── Bottom: loadout slots ───────────────────────────────────────
        float lsGap = 15f;
        float slotsStartX = -(loadoutSlotSize + lsGap);

        MakeTMP("LoadoutLabel", canvasGO.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -(bH / 2f + 10f)), new Vector2(300f, 22f),
            "Current Loadout", statsFontSize, Color.white, TextAlignmentOptions.Center);

        loadoutSlotImages = new Image[Grimoire.LoadoutSize];
        loadoutSlotNames = new TMP_Text[Grimoire.LoadoutSize];

        for (int i = 0; i < Grimoire.LoadoutSize; i++)
        {
            float sx = slotsStartX + i * (loadoutSlotSize + lsGap);
            var srt = MakeRT($"LSlot_{i}", canvasGO.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(sx, -(bH / 2f + 45f)), new Vector2(loadoutSlotSize, loadoutSlotSize));

            loadoutSlotImages[i] = srt.gameObject.AddComponent<Image>();
            loadoutSlotImages[i].sprite = slotSprite;
            loadoutSlotImages[i].preserveAspect = true;

            int slotIdx = i;
            var slotBtn = srt.gameObject.AddComponent<Button>();
            slotBtn.targetGraphic = loadoutSlotImages[i];
            slotBtn.onClick.AddListener(() => SetEquipTarget(slotIdx));

            loadoutSlotNames[i] = MakeTMP($"LName_{i}", srt,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(loadoutSlotSize + 10f, 40f),
                "", 10f, Color.white, TextAlignmentOptions.Center);
        }

        canvasGO.SetActive(false);
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    private void RefreshLibrary()
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null) return;

        // Clear old rows
        foreach (var row in spellRows)
            if (row.rt != null) Destroy(row.rt.gameObject);
        spellRows.Clear();

        // Build rows
        var allSpells = grimoire.AllSpells;
        for (int i = 0; i < allSpells.Count; i++)
        {
            SpellData spell = allSpells[i];
            float y = -(i * (rowH + rowGap));

            var row = new SpellRowUI { spell = spell };
            row.rt = MakeRT($"Row_{i}", libraryContainer,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(libraryContainer.sizeDelta.x - 10f, rowH));

            row.bg = row.rt.gameObject.AddComponent<Image>();
            row.bg.sprite = boxSprite;
            row.bg.type = Image.Type.Sliced;
            row.bg.color = new Color(1f, 0.99f, 0.92f);

            bool isCorrupted = spell.HasTag(SpellTag.SELF_DAMAGE)
                            || spell.HasTag(SpellTag.ENEMY_HOMING)
                            || spell.HasTag(SpellTag.REVERSED_CONTROLS);

            Color textColor = isCorrupted
                ? new Color(0.79f, 0.17f, 0.17f)
                : new Color(0.12f, 0.12f, 0.12f);

            string label = spell.spellName;
            if (isCorrupted) label += "  [corrupted]";
            if (spell.isMerged) label += "  [merged]";

            row.nameText = MakeTMP($"Name_{i}", row.rt,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(0f, rowH),
                label, listFontSize, textColor, TextAlignmentOptions.MidlineLeft);
            row.nameText.rectTransform.offsetMax = new Vector2(-10f, 0f);

            // Click to select
            var btn = row.rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.bg;
            SpellData captured = spell;
            btn.onClick.AddListener(() => SelectSpell(captured));

            spellRows.Add(row);
        }

        // Refresh loadout slots
        RefreshLoadoutSlots();

        // Select first spell if none selected
        if (selectedSpell == null && allSpells.Count > 0)
            SelectSpell(allSpells[0]);
        else if (selectedSpell != null)
            SelectSpell(selectedSpell); // refresh highlight
    }

    private void SelectSpell(SpellData spell)
    {
        selectedSpell = spell;

        // Highlight selected row
        foreach (var row in spellRows)
        {
            bool sel = row.spell == spell;
            row.bg.color = sel
                ? new Color(1f, 0.95f, 0.75f) // yellow highlight
                : new Color(1f, 0.99f, 0.92f);
        }

        // Populate right page
        if (spell == null)
        {
            detailName.text = "";
            detailElement.text = "";
            detailFlavor.text = "";
            detailCorruptionFlavor.text = "";
            detailTags.text = "";
            detailStats.text = "";
            equipBtnRT.gameObject.SetActive(false);
            return;
        }

        detailName.text = spell.spellName;

        bool isCorrupted = spell.HasTag(SpellTag.SELF_DAMAGE)
                        || spell.HasTag(SpellTag.ENEMY_HOMING)
                        || spell.HasTag(SpellTag.REVERSED_CONTROLS);
        detailName.color = isCorrupted
            ? new Color(0.79f, 0.17f, 0.17f)
            : Color.white;

        detailElement.text = !string.IsNullOrEmpty(spell.element)
            ? $"Element: {spell.element}" : "";

        detailFlavor.text = !string.IsNullOrEmpty(spell.flavor)
            ? $"\"{spell.flavor}\"" : "";

        detailCorruptionFlavor.text = !string.IsNullOrEmpty(spell.corruptionFlavor)
            ? spell.corruptionFlavor : "";

        string tags = "";
        if (spell.tags != null)
            foreach (var t in spell.tags) tags += t + "  ";
        detailTags.text = tags.TrimEnd();

        detailStats.text = $"DMG: {spell.damage:F0}   SPD: {spell.speed:F1}   CD: {spell.cooldown:F1}";

        equipBtnRT.gameObject.SetActive(true);
        equipBtnText.text = $"Equip to Slot {equipTargetSlot + 1}";
    }

    private void RefreshLoadoutSlots()
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null) return;

        for (int i = 0; i < Grimoire.LoadoutSize; i++)
        {
            SpellData s = grimoire.Loadout[i];
            bool isTarget = i == equipTargetSlot;
            loadoutSlotImages[i].sprite = isTarget ? slotSelectedSprite : slotSprite;
            loadoutSlotNames[i].text = s != null ? s.spellName : "Empty";
            loadoutSlotNames[i].color = s != null ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        }
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private void SetEquipTarget(int slot)
    {
        equipTargetSlot = slot;
        equipBtnText.text = $"Equip to Slot {slot + 1}";
        RefreshLoadoutSlots();
    }

    private void OnEquipClick()
    {
        if (selectedSpell == null) return;
        Grimoire.Instance?.EquipToSlot(selectedSpell, equipTargetSlot);
        RefreshLibrary();
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

    private TMP_Text MakeTMP(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size,
        string text, float fontSize, Color color, TextAlignmentOptions align)
    {
        return MakeTMP(name, MakeRT(name + "_wrap", parent, anchorMin, anchorMax, pivot, pos, size),
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            text, fontSize, color, align);
    }
}
