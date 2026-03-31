using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    [SerializeField] private float bookW = 1200f;
    [SerializeField] private float spellBoxSize = 80f;
    [SerializeField] private float spellBoxGap  = 10f;
    [SerializeField] private float titleFontSize = 26f;
    [SerializeField] private float detailNameFontSize = 28f;
    [SerializeField] private float detailBodyFontSize = 13f;
    [SerializeField] private float statsFontSize = 15f;
    [SerializeField] private float listFontSize = 15f;

    private GameObject canvasGO;
    private RectTransform _panelRoot;
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
    private GameObject equipSlotsGO;

    private SpellData selectedSpell;

    private class SpellRowUI
    {
        public RectTransform rt;
        public Image bg;
        public Image icon;
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
        canvasGO = new GameObject("GrimoireUI_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Backdrop — stays as direct child of canvas (does not animate)
        var backdrop = MakeRT("Backdrop", canvasGO.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        backdrop.offsetMin = Vector2.zero; backdrop.offsetMax = Vector2.zero;
        var bdImg = backdrop.gameObject.AddComponent<Image>();
        bdImg.color = new Color(0f, 0f, 0f, 0.7f);
        var bdBtn = backdrop.gameObject.AddComponent<Button>();
        bdBtn.targetGraphic = bdImg;
        bdBtn.onClick.AddListener(Close);

        // Panel root — wraps all visual book elements so they can animate together
        var panelRoot = MakeRT("PanelRoot", canvasGO.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        panelRoot.offsetMin = Vector2.zero;
        panelRoot.offsetMax = Vector2.zero;
        _panelRoot = panelRoot;

        // ── Book background ─────────────────────────────────────────────
        // Match rect to sprite aspect so the book fills the rect exactly
        float spriteAspect = bookSprite != null
            ? bookSprite.rect.width / bookSprite.rect.height
            : 1.805f;
        float bH = bookW / spriteAspect;

        // Book image — rendered 20% larger for visual weight
        float imageW = bookW * 1.2f;
        float imageH = imageW / spriteAspect;
        var bookImageRT = MakeRT("BookImage", panelRoot,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0f), new Vector2(imageW, imageH));
        var bookImg = bookImageRT.gameObject.AddComponent<Image>();
        bookImg.sprite = bookSprite;

        // Content layer — original dimensions so all text positions are unchanged
        var bookRT = MakeRT("BookContent", panelRoot,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0f), new Vector2(bookW, bH));

        // ── Page content areas (derived from sprite proportions) ────────
        // GrimoireBook.png (278×154): writable page areas approximately:
        //   Left page:  x 18..127    Right page: x 150..260    y: 10..120
        const float kBorderFrac = 0.065f;  // outer border (18/278)
        const float kSpineLFrac = 0.457f;  // left page right edge (127/278)
        const float kSpineRFrac = 0.540f;  // right page left edge (150/278)
        const float kTopFrac    = 0.065f;  // top border (10/154)
        const float kBotFrac    = 0.780f;  // bottom of writable area (120/154)
        const float kInnerPad   = 2f;      // breathing room per side

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
            "", 32f, Color.white, TextAlignmentOptions.Center);
        dy -= 30f;

        detailElement = MakeTMP("DElement", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 22f),
            "", 17f, new Color(0.9f, 0.47f, 0f), TextAlignmentOptions.Center);
        dy -= 24f;

        detailFlavor = MakeTMP("DFlavor", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 50f),
            "", 17f, new Color(0.35f, 0.35f, 0.35f), TextAlignmentOptions.Center);
        detailFlavor.fontStyle = FontStyles.Italic;
        dy -= 50f;

        detailCorruptionFlavor = MakeTMP("DCFlavor", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 28f),
            "", 17f, new Color(0.79f, 0.17f, 0.17f, 0.85f), TextAlignmentOptions.Center);
        detailCorruptionFlavor.fontStyle = FontStyles.Italic;
        dy -= 28f;

        detailTags = MakeTMP("DTags", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 85f),
            "", 15f, new Color(0.1f, 0.44f, 0.76f), TextAlignmentOptions.TopLeft);
        detailTags.textWrappingMode = TMPro.TextWrappingModes.Normal;
        detailTags.overflowMode = TextOverflowModes.Truncate;
        dy -= 87f;

        detailStats = MakeTMP("DStats", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 25f),
            "", 19f, new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);
        dy -= 42f;

        // Equip slot buttons (1 / 2 / 3)
        float slotBtnW   = Mathf.Min(88f, (pageW - 16f) / 3f);
        float slotBtnGap = 8f;
        float totalSlotW = 3f * slotBtnW + 2f * slotBtnGap;
        float slotStartX = rightPageX - totalSlotW / 2f + slotBtnW / 2f;

        var equipRT = MakeRT("EquipSlots", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(totalSlotW, 36f));
        equipSlotsGO = equipRT.gameObject;

        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            float bx = (i - 1) * (slotBtnW + slotBtnGap);
            var bRT = MakeRT($"SlotBtn_{i}", equipRT,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(bx, 0f), new Vector2(slotBtnW, 36f));
            var bImg = bRT.gameObject.AddComponent<Image>();
            bImg.sprite = buttonSprite;
            bImg.type = Image.Type.Sliced;
            bImg.color = new Color(0.72f, 0.58f, 0.42f);
            var btn = bRT.gameObject.AddComponent<Button>();
            btn.targetGraphic = bImg;
            btn.onClick.AddListener(() => OnEquipToSlot(slot));
            bRT.gameObject.AddComponent<UIButtonHover>();
            MakeTMP($"SlotLabel_{i}", bRT,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero,
                $"Slot {i + 1}", statsFontSize, Color.white, TextAlignmentOptions.Center);
        }

        equipSlotsGO.SetActive(false);

        // X close button — top-right of book image
        float closeBtnSize = 48f;
        var closeBtnRT = MakeRT("CloseBtn", panelRoot,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(imageW / 2f - closeBtnSize * 0.3f, imageH / 2f - closeBtnSize * 0.3f),
            new Vector2(closeBtnSize, closeBtnSize));
        var closeBtnImg = closeBtnRT.gameObject.AddComponent<Image>();
        closeBtnImg.color = new Color(0.15f, 0.1f, 0.05f, 0.85f);
        var closeBtn = closeBtnRT.gameObject.AddComponent<Button>();
        closeBtn.targetGraphic = closeBtnImg;
        closeBtn.onClick.AddListener(Close);
        closeBtnRT.gameObject.AddComponent<UIButtonHover>();
        MakeTMP("CloseBtnLabel", closeBtnRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "X", 24f, new Color(0.9f, 0.8f, 0.6f), TextAlignmentOptions.Center);

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

        // Build icon grid
        var allSpells = grimoire.AllSpells;

        float boxSize  = spellBoxSize;
        float gap      = spellBoxGap;
        float nameH    = 20f;
        float cellH    = boxSize + 4f + nameH;
        float contW    = libraryContainer.sizeDelta.x;
        int   cols     = Mathf.Max(1, Mathf.FloorToInt((contW + gap) / (boxSize + gap)));
        float gridW    = cols * boxSize + (cols - 1) * gap;
        float startX   = -gridW / 2f + boxSize / 2f;

        for (int i = 0; i < allSpells.Count; i++)
        {
            SpellData spell = allSpells[i];
            int c = i % cols;
            int r = i / cols;

            bool isCorrupted = spell.HasTag(SpellTag.SELF_DAMAGE)
                            || spell.HasTag(SpellTag.ENEMY_HOMING)
                            || spell.HasTag(SpellTag.REVERSED_CONTROLS);

            var row = new SpellRowUI { spell = spell };

            // Cell container (box + name)
            row.rt = MakeRT($"Cell_{i}", libraryContainer,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(startX + c * (boxSize + gap), -(r * (cellH + gap))),
                new Vector2(boxSize, cellH));

            // Square icon box
            var boxRT = MakeRT($"Box_{i}", row.rt,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(boxSize, boxSize));
            row.bg = boxRT.gameObject.AddComponent<Image>();
            row.bg.sprite = boxSprite;
            row.bg.type = Image.Type.Sliced;
            row.bg.color = isCorrupted
                ? new Color(0.38f, 0.12f, 0.12f)
                : new Color(1f, 0.99f, 0.92f);

            // Spell icon inside the box (inset)
            float iconInset = boxSize * 0.12f;
            float iconDim = boxSize - iconInset * 2f;
            var iconRT = MakeRT($"Icon_{i}", boxRT,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(iconDim, iconDim));
            row.icon = iconRT.gameObject.AddComponent<Image>();
            row.icon.preserveAspect = true;
            row.icon.raycastTarget = false;
            if (spell.icon != null)
            {
                row.icon.sprite = spell.icon;
                row.icon.enabled = true;
            }
            else
            {
                row.icon.enabled = false;
            }

            var btn = boxRT.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.bg;
            btn.transition = Selectable.Transition.None;
            SpellData captured = spell;
            btn.onClick.AddListener(() => SelectSpell(captured));
            boxRT.gameObject.AddComponent<UIButtonHover>();

            // Name label below the box
            Color textColor = isCorrupted
                ? new Color(0.79f, 0.17f, 0.17f)
                : new Color(0.12f, 0.12f, 0.12f);

            string label = spell.spellName;
            if (spell.isMerged) label += " *";

            row.nameText = MakeTMP($"Name_{i}", row.rt,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(boxSize + 4f)), new Vector2(boxSize + 10f, nameH),
                label, listFontSize * 0.8f, textColor, TextAlignmentOptions.Center);

            spellRows.Add(row);
        }

        // Select first spell if none selected
        if (selectedSpell == null && allSpells.Count > 0)
            SelectSpell(allSpells[0]);
        else if (selectedSpell != null)
            SelectSpell(selectedSpell); // refresh highlight
    }

    private void SelectSpell(SpellData spell)
    {
        selectedSpell = spell;

        // Highlight selected box
        foreach (var row in spellRows)
        {
            bool sel = row.spell == spell;
            bool corrupted = row.spell.HasTag(SpellTag.SELF_DAMAGE)
                          || row.spell.HasTag(SpellTag.ENEMY_HOMING)
                          || row.spell.HasTag(SpellTag.REVERSED_CONTROLS);
            row.bg.color = sel
                ? new Color(1f, 0.95f, 0.75f)
                : corrupted ? new Color(0.38f, 0.12f, 0.12f) : new Color(1f, 0.99f, 0.92f);
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
            if (equipSlotsGO != null) equipSlotsGO.SetActive(false);
            return;
        }

        if (equipSlotsGO != null) equipSlotsGO.SetActive(true);

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
            foreach (var t in spell.tags)
            {
                string desc = GetTagDescription(t);
                if (desc != null) tags += $"• {desc}\n";
            }
        detailTags.text = tags.TrimEnd();

        detailStats.text = $"DMG: {spell.damage:F0}   SPD: {spell.speed:F1}   CD: {spell.cooldown:F1}";
        detailStats.color = isCorrupted
            ? new Color(0.79f, 0.17f, 0.17f)
            : new Color(0.2f, 0.2f, 0.2f);
    }

    private void OnEquipToSlot(int slot)
    {
        if (selectedSpell == null) return;
        Grimoire.Instance?.EquipToSlot(selectedSpell, slot);
    }

    // ── Tag Descriptions ────────────────────────────────────────────────────

    private static readonly Dictionary<SpellTag, string> tagDescriptions = new()
    {
        // Movement
        { SpellTag.PROJECTILE, "Fires a projectile" },
        { SpellTag.ORBITAL, "Orbits around you" },
        { SpellTag.BEAM, "Instant beam attack" },
        { SpellTag.CHANNELED, "Steerable while held" },
        // Trajectory
        { SpellTag.HOMING, "Homes toward enemies" },
        { SpellTag.WALL_BOUNCE, "Bounces off walls (3x)" },
        { SpellTag.REFLECTING, "Bounces off walls endlessly" },
        { SpellTag.PIERCE_WALLS, "Passes through walls" },
        { SpellTag.SPIRAL, "Corkscrews through the air" },
        { SpellTag.STUTTER_MOTION, "Stop-start movement" },
        { SpellTag.BOOMERANG, "Returns to you" },
        { SpellTag.SURFACE_CRAWLING, "Slides along walls" },
        { SpellTag.SKIPPING, "Bounces along the ground" },
        { SpellTag.DELAYED_ARC, "Swells at midpoint, then accelerates" },
        { SpellTag.SENTIENT, "Hunts enemies on its own" },
        { SpellTag.DELAYED, "Activates after a delay" },
        { SpellTag.PHASING, "Phases in and out of reality" },
        { SpellTag.PERSISTENT, "Stays until it hits something" },
        // Caster
        { SpellTag.DOUBLE_HIT, "Fires a second copy" },
        { SpellTag.MIRRORED, "Also fires backwards" },
        { SpellTag.GHOST_CAST, "Fires an invisible damage copy" },
        { SpellTag.SACRIFICE, "Costs HP, deals 2x damage" },
        { SpellTag.ECHOING, "Re-casts itself after 3s" },
        // On impact
        { SpellTag.CHAIN, "Jumps between enemies" },
        { SpellTag.FRAGMENTING, "Shatters into fragments on hit" },
        { SpellTag.AOE_BURST, "Explodes on hit" },
        { SpellTag.PIERCE, "Passes through enemies" },
        { SpellTag.LIFESTEAL, "Heals you on hit" },
        { SpellTag.PUSH, "Knocks enemies back" },
        { SpellTag.PULL, "Pulls enemies toward you" },
        { SpellTag.DETONATING, "Embeds then explodes" },
        { SpellTag.LINGERING, "Leaves a damage zone" },
        { SpellTag.SWAPPING, "Teleports you to impact" },
        { SpellTag.CONTAGIOUS, "Spreads to nearby enemies" },
        { SpellTag.BURROWING, "Burrows then erupts under enemies" },
        { SpellTag.TETHERED, "Tethers and pulls enemy to you" },
        { SpellTag.SPLIT_ON_IMPACT, "Splits into 3 on hit" },
        // Status
        { SpellTag.SLOW, "Slows enemies" },
        { SpellTag.BURN, "Burns over time" },
        { SpellTag.FREEZE, "Freezes enemies" },
        { SpellTag.STUN, "Stuns enemies" },
        { SpellTag.POISON, "Poisons (stacks 3x)" },
        { SpellTag.BLEED, "Escalating bleed damage" },
        { SpellTag.ROOT, "Roots enemies in place" },
        { SpellTag.WEAKNESS, "Enemies take more damage" },
        { SpellTag.CURSE, "Enemies flee in fear" },
        { SpellTag.BLIND, "Blinds enemies" },
        // Corruption
        { SpellTag.SELF_DAMAGE, "Hurts you on cast" },
        { SpellTag.ENEMY_HOMING, "Homes toward YOU" },
        { SpellTag.REVERSED_CONTROLS, "Aim is reversed" },
        // Meta
        { SpellTag.PROBABILITY, "Random effect each cast" },
    };

    private static string GetTagDescription(SpellTag tag)
    {
        return tagDescriptions.TryGetValue(tag, out var desc) ? desc : null;
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
