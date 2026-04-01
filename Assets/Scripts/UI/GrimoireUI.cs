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
    [SerializeField] private float bookW = 1520f;
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

    /// <summary>The Grimoire canvas GO for sorting order manipulation.</summary>
    public GameObject CanvasGO => canvasGO;

    // Left page: library list
    private RectTransform libraryContainer;
    private float _libPageW;
    private readonly List<SpellRowUI> spellRows = new();

    // Right page: detail view
    private TMP_Text detailName;
    private TMP_Text detailElement;
    private TMP_Text detailFlavor;
    private TMP_Text detailCorruptionFlavor;
    private TMP_Text detailTags;
    private TMP_Text detailStats;

    // Right page: place + delete buttons
    private Button   _placeBtn;
    private TMP_Text _placeBtnLabel;
    private Button   _deleteBtn;

    // Placement mode
    private SpellData  _placeSpell;
    private GameObject _overlayCanvasGO;

    private SpellData selectedSpell;

    /// <summary>Currently selected spell in the Grimoire UI (null if none).</summary>
    public SpellData SelectedSpell => selectedSpell;
    /// <summary>True when the player is in placement mode (choosing a hotbar slot).</summary>
    public bool InPlacementMode => _placeSpell != null;
    /// <summary>Set to true to disable the Delete button (e.g. during tutorial).</summary>
    public bool DeleteDisabled { get; set; }

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

    // Reopens without adding another Pause — used after placement mode
    // (game is already paused from the original Open() call).
    private void ReopenAfterPlacement()
    {
        if (canvasGO == null) Build();
        isOpen = true;
        canvasGO.SetActive(true);
        // Do NOT call PauseManager.Pause() — still at count 1 from the original Open()
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
        DestroyPlacementOverlay();
        SettingsData.OnBindingsChanged -= OnBindingsChangedStub;
    }

    // Stub — kept so the unsubscribe in OnDestroy is safe even if never subscribed
    private void OnBindingsChangedStub() { }

    private void Update()
    {
        if (_overlayCanvasGO != null && Input.GetKeyDown(KeyCode.Escape))
            OnPlacementCancelled();
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

        _libPageW = pageW;

        // ScrollRect for overflow support
        var scrollRT = MakeRT("LibScroll", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(leftPageX, -(topOffset + 40f)), new Vector2(pageW, pageH - 50f));

        var viewportRT = MakeRT("Viewport", scrollRT,
            Vector2.zero, Vector2.one, new Vector2(0f, 1f),
            Vector2.zero, Vector2.zero);
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        viewportRT.gameObject.AddComponent<RectMask2D>();

        libraryContainer = MakeRT("LibContent", viewportRT,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, pageH - 50f));

        var sr = scrollRT.gameObject.AddComponent<ScrollRect>();
        sr.content          = libraryContainer;
        sr.viewport         = viewportRT;
        sr.horizontal       = false;
        sr.vertical         = true;
        sr.scrollSensitivity = 30f;
        sr.movementType     = ScrollRect.MovementType.Clamped;

        // ── Right page: spell details ───────────────────────────────────
        MakeTMP("DetailTitle", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, -topOffset), new Vector2(pageW, 30f),
            "~ Spell Details ~", titleFontSize, new Color(0.12f, 0.12f, 0.12f), TextAlignmentOptions.Center);

        float dy = -(topOffset + 40f);

        detailName = MakeTMP("DName", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 36f),
            "", 38f, Color.white, TextAlignmentOptions.Center);
        dy -= 38f;

        detailElement = MakeTMP("DElement", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 24f),
            "", 21f, new Color(0.9f, 0.47f, 0f), TextAlignmentOptions.Center);
        dy -= 26f;

        detailFlavor = MakeTMP("DFlavor", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(rightPageX, dy), new Vector2(pageW, 56f),
            "", 19f, new Color(0.35f, 0.35f, 0.35f), TextAlignmentOptions.Center);
        detailFlavor.fontStyle = FontStyles.Italic;
        dy -= 58f;

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
            "", 20f, new Color(0.2f, 0.2f, 0.2f), TextAlignmentOptions.Center);
        dy -= 36f;

        // Place in Grimoire button (left ~60%) + Delete button (right ~36%), side by side
        float rowGap  = 8f;
        float placeW  = pageW * 0.58f;
        float deleteW = pageW * 0.36f;
        // Center each button within the row (row is centered at rightPageX)
        float placeCx  = rightPageX - deleteW / 2f - rowGap / 2f;
        float deleteCx = rightPageX + placeW  / 2f + rowGap / 2f;

        var placeBtnRT = MakeRT("PlaceBtn", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(placeCx, dy), new Vector2(placeW, 36f));
        var placeBg = placeBtnRT.gameObject.AddComponent<Image>();
        placeBg.color  = new Color(0.15f, 0.52f, 0.18f, 0.88f);
        _placeBtn      = placeBtnRT.gameObject.AddComponent<Button>();
        _placeBtn.targetGraphic = placeBg;
        _placeBtn.interactable  = false;
        _placeBtn.onClick.AddListener(() => StartPlacementMode(selectedSpell));
        placeBtnRT.gameObject.AddComponent<UIButtonHover>();
        _placeBtnLabel = MakeTMP("PlaceBtnLabel", placeBtnRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "Place in Grimoire", 14f, Color.white, TextAlignmentOptions.Center);

        var deleteBtnRT = MakeRT("DeleteBtn", bookRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(deleteCx, dy), new Vector2(deleteW, 36f));
        var deleteBg = deleteBtnRT.gameObject.AddComponent<Image>();
        deleteBg.color  = new Color(0.52f, 0.12f, 0.12f, 0.88f);
        _deleteBtn      = deleteBtnRT.gameObject.AddComponent<Button>();
        _deleteBtn.targetGraphic = deleteBg;
        _deleteBtn.interactable  = false;
        _deleteBtn.onClick.AddListener(DeleteSelectedSpell);
        deleteBtnRT.gameObject.AddComponent<UIButtonHover>();
        MakeTMP("DeleteBtnLabel", deleteBtnRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "Delete (+HP)", 13f, Color.white, TextAlignmentOptions.Center);

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

    // ── Placement mode ────────────────────────────────────────────────────────

    private void StartPlacementMode(SpellData spell)
    {
        if (spell == null) return;
        _placeSpell = spell;
        // Hide grimoire canvas without unpausing (game stays paused)
        isOpen = false;
        if (_panelRoot != null) StartCoroutine(HideForPlacementRoutine());
        else { canvasGO.SetActive(false); ActivatePlacementOverlay(); }
    }

    private System.Collections.IEnumerator HideForPlacementRoutine()
    {
        if (_panelRoot != null)
            yield return StartCoroutine(UIPanelAnimator.AnimateOut(_panelRoot));
        canvasGO.SetActive(false);
        ActivatePlacementOverlay();
    }

    private void ActivatePlacementOverlay()
    {
        ShowPlacementOverlay();
        SpellHotbar.Instance?.EnterPlacementMode(_placeSpell, OnPlacementSlotSelected);
    }

    private void ShowPlacementOverlay()
    {
        _overlayCanvasGO = new GameObject("PlacementOverlay_Canvas");
        var canvas          = _overlayCanvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 89; // below hotbar (90)

        var scaler = _overlayCanvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        _overlayCanvasGO.AddComponent<GraphicRaycaster>();

        // Full-screen dark background — click anywhere to cancel
        var bgRT = MakeRT("OverlayBg", _overlayCanvasGO.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
        var bgImg  = bgRT.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.58f);
        var bgBtn  = bgRT.gameObject.AddComponent<Button>();
        bgBtn.targetGraphic = bgImg;
        bgBtn.onClick.AddListener(OnPlacementCancelled);

        // Instruction label
        MakeTMP("ChooseLabel", _overlayCanvasGO.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 120f), new Vector2(700f, 44f),
            "~ Click a glowing slot to equip ~",
            24f, new Color(1f, 0.95f, 0.8f, 0.92f), TextAlignmentOptions.Center);

        // Cancel button — centered on screen
        var cancelRT  = MakeRT("CancelBtn", _overlayCanvasGO.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(220f, 46f));
        var cancelBg   = cancelRT.gameObject.AddComponent<Image>();
        cancelBg.color = new Color(0.52f, 0.1f, 0.1f, 0.9f);
        var cancelBtn  = cancelRT.gameObject.AddComponent<Button>();
        cancelBtn.targetGraphic = cancelBg;
        cancelBtn.onClick.AddListener(OnPlacementCancelled);
        cancelRT.gameObject.AddComponent<UIButtonHover>();
        MakeTMP("CancelLabel", cancelRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "✕  Cancel", 18f, Color.white, TextAlignmentOptions.Center);
    }

    private void DestroyPlacementOverlay()
    {
        if (_overlayCanvasGO != null) { Destroy(_overlayCanvasGO); _overlayCanvasGO = null; }
    }

    private void OnPlacementSlotSelected(int slot)
    {
        if (slot == -1) UltimateAbility.Instance?.SetSpell(_placeSpell);
        else            Grimoire.Instance?.EquipToSlot(_placeSpell, slot);

        SFXManager.Instance?.PlayEquipSpell();
        SpellHotbar.Instance?.ExitPlacementMode();
        DestroyPlacementOverlay();
        _placeSpell = null;
        ReopenAfterPlacement(); // game was already paused — don't double-count
    }

    private void OnPlacementCancelled()
    {
        SpellHotbar.Instance?.ExitPlacementMode();
        DestroyPlacementOverlay();
        _placeSpell = null;
        ReopenAfterPlacement(); // game was already paused — don't double-count
    }

    private void DeleteSelectedSpell()
    {
        if (selectedSpell == null) return;
        SpellData toDelete = selectedSpell;
        float healAmount = toDelete.damage;

        // Remove from grimoire library (also unequips from loadout if slotted)
        Grimoire.Instance?.RemoveSpell(toDelete);

        // If it was the ultimate, also clear UltimateAbility
        if (toDelete.tier == SpellTier.Ultimate && UltimateAbility.Instance?.Spell == toDelete)
            UltimateAbility.Instance?.SetSpell(null);

        // Heal player by the spell's damage value
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            var hp = playerObj.GetComponent<Health>();
            hp?.Heal(healAmount);
        }

        selectedSpell = null;
        RefreshLibrary();
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    private void RefreshLibrary()
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null) return;

        // Clear all children (spell cells + tier dividers)
        foreach (Transform child in libraryContainer)
            Destroy(child.gameObject);
        spellRows.Clear();

        var allSpells = grimoire.AllSpells;

        float boxSize = spellBoxSize;
        float gap     = spellBoxGap;
        float nameH   = 20f;
        float cellH   = boxSize + 4f + nameH;
        float contW   = _libPageW > 0f ? _libPageW : libraryContainer.sizeDelta.x;
        int   cols    = Mathf.Max(1, Mathf.FloorToInt((contW + gap) / (boxSize + gap)));
        float gridW   = cols * boxSize + (cols - 1) * gap;
        float startX  = -gridW / 2f + boxSize / 2f;

        SpellTier[] tiers     = { SpellTier.Basic, SpellTier.Skill, SpellTier.Ultimate };
        Color[]     tierColors = {
            new Color(0.15f, 0.75f, 0.25f),
            new Color(0.25f, 0.55f, 1.0f),
            new Color(0.95f, 0.68f, 0.1f),
        };
        string[] tierLabels = { "── BASIC ──", "── SKILL ──", "── ULTIMATE ──" };

        float cursorY    = 0f;
        const float divH    = 20f;
        const float secGap  = 10f;
        int   spellIndex = 0;

        for (int ti = 0; ti < tiers.Length; ti++)
        {
            SpellTier tier = tiers[ti];
            var tierSpells = new System.Collections.Generic.List<SpellData>();
            foreach (var s in allSpells)
                if (s.tier == tier) tierSpells.Add(s);

            if (tierSpells.Count == 0) continue;

            // Section divider header
            MakeTMP($"Div_{tier}", libraryContainer,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, cursorY), new Vector2(contW, divH),
                tierLabels[ti], 9f, tierColors[ti], TextAlignmentOptions.Center);
            cursorY -= divH + 4f;

            // Spell icon grid for this tier
            for (int i = 0; i < tierSpells.Count; i++)
            {
                SpellData spell = tierSpells[i];
                int c = i % cols;
                int r = i / cols;

                bool isCorrupted = spell.HasTag(SpellTag.SELF_DAMAGE)
                                || spell.HasTag(SpellTag.ENEMY_HOMING)
                                || spell.HasTag(SpellTag.REVERSED_CONTROLS);

                var row = new SpellRowUI { spell = spell };

                row.rt = MakeRT($"Cell_{spellIndex}", libraryContainer,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(startX + c * (boxSize + gap), cursorY - r * (cellH + gap)),
                    new Vector2(boxSize, cellH));

                var boxRT = MakeRT($"Box_{spellIndex}", row.rt,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    Vector2.zero, new Vector2(boxSize, boxSize));
                row.bg = boxRT.gameObject.AddComponent<Image>();
                row.bg.sprite = boxSprite;
                row.bg.type   = Image.Type.Sliced;
                row.bg.color  = isCorrupted
                    ? new Color(0.38f, 0.12f, 0.12f)
                    : new Color(1f, 0.99f, 0.92f);

                float iconInset = boxSize * 0.12f;
                float iconDim   = boxSize - iconInset * 2f;
                var iconRT = MakeRT($"Icon_{spellIndex}", boxRT,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(iconDim, iconDim));
                row.icon = iconRT.gameObject.AddComponent<Image>();
                row.icon.preserveAspect = true;
                row.icon.raycastTarget  = false;
                if (spell.icon != null)
                {
                    row.icon.sprite  = spell.icon;
                    row.icon.enabled = true;
                }
                else
                {
                    row.icon.enabled = false;
                }

                bool isEquipped = IsSpellEquipped(spell);
                row.bg.color = isEquipped
                    ? new Color(0.12f, 0.52f, 0.18f, 0.85f)
                    : isCorrupted ? new Color(0.38f, 0.12f, 0.12f) : new Color(1f, 0.99f, 0.92f);

                var btn = boxRT.gameObject.AddComponent<Button>();
                btn.targetGraphic = row.bg;
                btn.transition    = Selectable.Transition.None;
                SpellData captured = spell;
                btn.onClick.AddListener(() => SelectSpell(captured));
                boxRT.gameObject.AddComponent<UIButtonHover>();

                // Small tier badge in top-left corner of box
                MakeTMP($"TierBadge_{spellIndex}", boxRT,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(3f, -3f), new Vector2(14f, 14f),
                    spell.tier == SpellTier.Ultimate ? "U" : spell.tier.ToString()[0].ToString(),
                    9f, tierColors[ti], TextAlignmentOptions.Center);

                Color textColor = isCorrupted
                    ? new Color(0.79f, 0.17f, 0.17f)
                    : new Color(0.12f, 0.12f, 0.12f);

                string label = spell.spellName;
                if (spell.isMerged) label += " *";

                row.nameText = MakeTMP($"Name_{spellIndex}", row.rt,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -(boxSize + 4f)), new Vector2(boxSize + 10f, nameH),
                    label, listFontSize * 0.8f, textColor, TextAlignmentOptions.Center);

                spellRows.Add(row);
                spellIndex++;
            }

            int numRows = Mathf.CeilToInt((float)tierSpells.Count / cols);
            cursorY -= numRows * (cellH + gap) + secGap;
        }

        // Resize content height to fit all sections
        libraryContainer.sizeDelta = new Vector2(0f, Mathf.Abs(cursorY) + 10f);

        if (selectedSpell == null && allSpells.Count > 0)
            SelectSpell(allSpells[0]);
        else if (selectedSpell != null)
            SelectSpell(selectedSpell);
    }

    private void SelectSpell(SpellData spell)
    {
        selectedSpell = spell;

        // Highlight selected box; preserve equipped (green) tint on non-selected
        foreach (var row in spellRows)
        {
            bool sel = row.spell == spell;
            bool corrupted = row.spell.HasTag(SpellTag.SELF_DAMAGE)
                          || row.spell.HasTag(SpellTag.ENEMY_HOMING)
                          || row.spell.HasTag(SpellTag.REVERSED_CONTROLS);
            bool equipped = IsSpellEquipped(row.spell);
            row.bg.color = sel
                ? new Color(1f, 0.95f, 0.75f)
                : equipped  ? new Color(0.12f, 0.52f, 0.18f, 0.85f)
                : corrupted ? new Color(0.38f, 0.12f, 0.12f)
                : new Color(1f, 0.99f, 0.92f);
        }

        // Enable/disable action buttons
        if (_placeBtn   != null) _placeBtn.interactable   = spell != null;
        if (_deleteBtn  != null) _deleteBtn.interactable  = spell != null && !DeleteDisabled;

        // Populate right page
        if (spell == null)
        {
            detailName.text = "";
            detailElement.text = "";
            detailFlavor.text = "";
            detailCorruptionFlavor.text = "";
            detailTags.text = "";
            detailStats.text = "";
            return;
        }

        detailName.text = spell.spellName;

        bool isCorrupted = spell.HasTag(SpellTag.SELF_DAMAGE)
                        || spell.HasTag(SpellTag.ENEMY_HOMING)
                        || spell.HasTag(SpellTag.REVERSED_CONTROLS);
        detailName.color = isCorrupted
            ? new Color(0.79f, 0.17f, 0.17f)
            : Color.white;

        // Tier display with color
        string tierName = spell.tier.ToString().ToUpper();
        Color tierColor = spell.tier switch
        {
            SpellTier.Basic    => new Color(0.25f, 0.85f, 0.35f),
            SpellTier.Skill    => new Color(0.35f, 0.65f, 1.0f),
            SpellTier.Ultimate => new Color(1.0f,  0.72f, 0.15f),
            _                  => Color.white,
        };
        string elementPart = !string.IsNullOrEmpty(spell.element) ? $"  |  {spell.element}" : "";
        detailElement.text = $"[{tierName}]{elementPart}";
        detailElement.color = tierColor;

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

    // ── Tag Descriptions ────────────────────────────────────────────────────

    private static bool IsSpellEquipped(SpellData spell)
    {
        if (spell == null) return false;
        var g = Grimoire.Instance;
        if (g != null)
            foreach (var s in g.Loadout)
                if (s == spell) return true;
        return UltimateAbility.Instance?.Spell == spell;
    }

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
