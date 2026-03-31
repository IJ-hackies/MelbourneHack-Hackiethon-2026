using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Settings overlay — tabs for Controls (rebindable keys) and Volume (music + SFX sliders).
/// Works in both the main menu (no Resume) and dungeon scenes (with Resume, pauses time).
///
/// Setup:
///   1. Add to any persistent or scene GameObject.
///   2. Assign font in Inspector.
///   3. Call Open(isDungeon) from HudIconBar / MainMenuController.
/// </summary>
public class SettingsUI : MonoBehaviour
{
    [Header("Font")]
    [SerializeField] private TMP_FontAsset font;

    // ── Colours (parchment theme matching Grimoire) ───────────────────────────
    private static readonly Color ColBg         = new Color(0f,    0f,    0f,    0.78f);
    private static readonly Color ColPanel      = new Color(0.16f, 0.12f, 0.08f, 0.97f);
    private static readonly Color ColParchment  = new Color(0.93f, 0.87f, 0.72f, 1f);
    private static readonly Color ColDarkBrown  = new Color(0.22f, 0.16f, 0.09f, 1f);
    private static readonly Color ColGold       = new Color(0.85f, 0.65f, 0.15f, 1f);
    private static readonly Color ColGoldDim    = new Color(0.55f, 0.40f, 0.08f, 1f);
    private static readonly Color ColTabActive  = new Color(0.80f, 0.58f, 0.12f, 1f);
    private static readonly Color ColTabIdle    = new Color(0.32f, 0.24f, 0.14f, 1f);
    private static readonly Color ColRow        = new Color(0.24f, 0.18f, 0.10f, 1f);
    private static readonly Color ColRowAlt     = new Color(0.20f, 0.15f, 0.08f, 1f);
    private static readonly Color ColBtnDanger  = new Color(0.55f, 0.12f, 0.08f, 1f);
    private static readonly Color ColBtnResume  = new Color(0.15f, 0.45f, 0.18f, 1f);

    // ── Layout constants ──────────────────────────────────────────────────────
    private const float PanelW    = 820f;
    private const float PanelH    = 630f;
    private const float HeaderH   = 58f;
    private const float TabBarH   = 44f;
    private const float ContentH  = PanelH - HeaderH - TabBarH - 80f; // 80 = footer
    private const float RowH      = 48f;
    private const float BtnH      = 44f;
    private const float BtnW      = 210f;
    private const float Pad       = 24f;

    // ── State ─────────────────────────────────────────────────────────────────
    private GameObject    _canvas;
    private RectTransform _panelRoot;
    private bool          _isDungeon;
    public  bool          IsOpen { get; private set; }

    private enum Tab { Controls, Volume }
    private Tab _activeTab = Tab.Controls;

    // Tab content root GameObjects
    private GameObject _controlsContent;
    private GameObject _volumeContent;

    // Tab buttons
    private Image    _tabControlsBg;
    private TMP_Text _tabControlsText;
    private Image    _tabVolumeBg;
    private TMP_Text _tabVolumeText;

    // Keybind rebinding
    private string         _listeningAction = null;
    private Image          _listeningBtnBg;
    private TMP_Text       _listeningBtnText;
    private readonly Dictionary<string, (Image bg, TMP_Text label)> _bindBtns = new();

    // ── Singleton ─────────────────────────────────────────────────────────────

    public static SettingsUI Instance { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Must be root-level for DontDestroyOnLoad
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        // Restore saved settings (volumes + keybinds) from PlayerPrefs
        SettingsData.Load();
        BuildCanvas();
        _canvas.SetActive(false);
    }

    private void Update()
    {
        if (!IsOpen) return;

        // Escape cancels rebind only (close via X button instead — Escape exits fullscreen on itch.io)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_listeningAction != null) CancelRebind();
            return;
        }

        if (_listeningAction == null) return;

        // Capture any key or mouse button press
        if (!Input.anyKeyDown) return;

        KeyCode captured = KeyCode.None;
        foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (k == KeyCode.Escape) continue;
            if (Input.GetKeyDown(k)) { captured = k; break; }
        }
        if (captured == KeyCode.None) return;

        SettingsData.SetKey(_listeningAction, captured);
        FinishRebind(captured);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Open(bool isDungeon)
    {
        _isDungeon = isDungeon;
        IsOpen     = true;
        _canvas.SetActive(true);

        SwitchTab(_activeTab);
        RefreshAllBindLabels();

        if (isDungeon) PauseManager.Pause();
        if (_panelRoot != null)
            StartCoroutine(UIPanelAnimator.AnimateIn(_panelRoot));
    }

    public void Close()
    {
        if (!IsOpen) return;
        if (_listeningAction != null) CancelRebind();
        IsOpen = false;
        StartCoroutine(CloseRoutine());
    }

    private System.Collections.IEnumerator CloseRoutine()
    {
        if (_panelRoot != null)
            yield return StartCoroutine(UIPanelAnimator.AnimateOut(_panelRoot));
        _canvas.SetActive(false);
        if (_isDungeon) PauseManager.Unpause();
    }

    public void Toggle(bool isDungeon)
    {
        if (IsOpen) Close();
        else        Open(isDungeon);
    }

    // ── Canvas construction ───────────────────────────────────────────────────

    private void BuildCanvas()
    {
        _canvas = new GameObject("SettingsUI_Canvas");
        _canvas.transform.SetParent(transform, false);

        var canvas          = _canvas.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler                  = _canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution  = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight   = 0.5f;

        _canvas.AddComponent<GraphicRaycaster>();

        // Backdrop
        var backdropRT        = MakeRT("Backdrop", _canvas.transform, V2.zero, V2.one, V2.half, V2.zero, V2.zero);
        backdropRT.offsetMin  = V2.zero;
        backdropRT.offsetMax  = V2.zero;
        var backdropImg       = backdropRT.gameObject.AddComponent<Image>();
        backdropImg.color     = ColBg;
        AddButton(backdropRT.gameObject, () => { if (_listeningAction != null) CancelRebind(); });

        // Panel
        var panelRT = MakeRT("Panel", _canvas.transform,
            V2.half, V2.half, V2.half, V2.zero, new Vector2(PanelW, PanelH));
        var panelImg    = panelRT.gameObject.AddComponent<Image>();
        panelImg.color  = ColPanel;
        _panelRoot = panelRT;

        BuildHeader(panelRT);
        BuildTabBar(panelRT);
        BuildControlsContent(panelRT);
        BuildVolumeContent(panelRT);
        BuildFooter(panelRT);
    }

    private void BuildHeader(RectTransform panel)
    {
        var hRT = MakeRT("Header", panel,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            V2.zero, new Vector2(0f, HeaderH));

        var bg    = hRT.gameObject.AddComponent<Image>();
        bg.color  = new Color(0.10f, 0.07f, 0.03f, 1f);

        MakeText("Title", hRT, V2.zero, V2.one, V2.half, V2.zero, V2.zero,
            "SETTINGS", 30f, ColGold, TextAlignmentOptions.Center, FontStyles.Bold);

        // X close button — top-right of header
        float closeBtnSize = 40f;
        var closeBtnRT = MakeRT("CloseBtn", hRT,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-10f, 0f), new Vector2(closeBtnSize, closeBtnSize));
        var closeBtnImg = closeBtnRT.gameObject.AddComponent<Image>();
        closeBtnImg.color = ColBtnDanger;
        var closeBtn = closeBtnRT.gameObject.AddComponent<Button>();
        closeBtn.targetGraphic = closeBtnImg;
        closeBtn.onClick.AddListener(Close);
        closeBtnRT.gameObject.AddComponent<UIButtonHover>();
        MakeText("CloseBtnLabel", closeBtnRT, V2.zero, V2.one, V2.half, V2.zero, V2.zero,
            "X", 22f, ColParchment, TextAlignmentOptions.Center, FontStyles.Bold);

        // Thin gold line under header
        var lineRT   = MakeRT("Line", panel,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -HeaderH), new Vector2(0f, 2f));
        var lineImg  = lineRT.gameObject.AddComponent<Image>();
        lineImg.color = ColGold;
    }

    private void BuildTabBar(RectTransform panel)
    {
        float tabBarY = -(HeaderH + 2f);
        var barRT = MakeRT("TabBar", panel,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, tabBarY), new Vector2(0f, TabBarH));

        float tabW = PanelW / 2f;

        // Controls tab
        var ctRT  = MakeRT("Controls", barRT,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            V2.zero, new Vector2(tabW, 0f));
        _tabControlsBg          = ctRT.gameObject.AddComponent<Image>();
        _tabControlsBg.color    = ColTabActive;
        _tabControlsText        = MakeText("Label", ctRT, V2.zero, V2.one, V2.half,
            V2.zero, V2.zero, "CONTROLS", 18f, ColParchment, TextAlignmentOptions.Center, FontStyles.Bold);
        AddButton(ctRT.gameObject, () => SwitchTab(Tab.Controls));
        ctRT.gameObject.AddComponent<UIButtonHover>();

        // Volume tab
        var vtRT = MakeRT("Volume", barRT,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f),
            V2.zero, new Vector2(tabW, 0f));
        _tabVolumeBg         = vtRT.gameObject.AddComponent<Image>();
        _tabVolumeBg.color   = ColTabIdle;
        _tabVolumeText       = MakeText("Label", vtRT, V2.zero, V2.one, V2.half,
            V2.zero, V2.zero, "VOLUME", 18f, new Color(0.7f, 0.62f, 0.45f), TextAlignmentOptions.Center, FontStyles.Bold);
        AddButton(vtRT.gameObject, () => SwitchTab(Tab.Volume));
        vtRT.gameObject.AddComponent<UIButtonHover>();

        // Divider line under tab bar
        var divRT  = MakeRT("Divider", panel,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -(HeaderH + 2f + TabBarH)), new Vector2(0f, 1f));
        divRT.gameObject.AddComponent<Image>().color = ColGoldDim;
    }

    private void BuildControlsContent(RectTransform panel)
    {
        float topY   = -(HeaderH + 2f + TabBarH + 1f);
        float height = PanelH - HeaderH - 2f - TabBarH - 1f - 80f;

        (string label, string action)[] rows =
        {
            ("Move Up",    "MoveUp"),
            ("Move Down",  "MoveDown"),
            ("Move Left",  "MoveLeft"),
            ("Move Right", "MoveRight"),
            ("Attack",     "Attack"),
            ("Spell 1",    "Slot1"),
            ("Spell 2",    "Slot2"),
            ("Spell 3",    "Slot3"),
            ("Dash",       "Dash"),
            ("Toggle Map", "ToggleMap"),
        };

        const float SbW = 10f; // scrollbar strip width

        // ── ScrollRect container ──────────────────────────────────────────────
        var scrollRT = MakeRT("ControlsContent", panel,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, topY), new Vector2(0f, height));
        _controlsContent = scrollRT.gameObject;

        // Transparent backing image required by ScrollRect
        var scrollBg   = _controlsContent.AddComponent<Image>();
        scrollBg.color = new Color(0f, 0f, 0f, 0f);

        var scroll               = _controlsContent.AddComponent<ScrollRect>();
        scroll.horizontal        = false;
        scroll.vertical          = true;
        scroll.movementType      = ScrollRect.MovementType.Clamped;
        scroll.inertia           = true;
        scroll.decelerationRate  = 0.15f;
        scroll.scrollSensitivity = 40f;

        // ── Viewport (clips overflowing rows) ─────────────────────────────────
        var vpRT = MakeRT("Viewport", scrollRT,
            Vector2.zero, Vector2.one, new Vector2(0f, 1f),
            Vector2.zero, new Vector2(-SbW - 2f, 0f)); // shrink right to make room for scrollbar
        var vpImg   = vpRT.gameObject.AddComponent<Image>();
        vpImg.color = Color.white; // must be opaque so the Mask stencil write reveals children
        var vpMask  = vpRT.gameObject.AddComponent<Mask>();
        vpMask.showMaskGraphic = false; // hides the white image visually, stencil still works
        scroll.viewport = vpRT;

        // ── Scrollable content (holds all rows) ───────────────────────────────
        float contentH = rows.Length * RowH;
        var contentRT  = MakeRT("Content", vpRT,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            Vector2.zero, new Vector2(0f, contentH));
        scroll.content = contentRT;

        for (int i = 0; i < rows.Length; i++)
            BuildBindRow(contentRT, rows[i].label, rows[i].action, -i * RowH,
                         i % 2 == 0 ? ColRow : ColRowAlt);

        // ── Scrollbar ─────────────────────────────────────────────────────────
        var sbRT  = MakeRT("ScrollbarV", scrollRT,
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
            Vector2.zero, new Vector2(SbW, 0f));
        sbRT.gameObject.AddComponent<Image>().color = new Color(0.18f, 0.13f, 0.07f, 1f);

        // Sliding area — inset 1px on each end so the handle doesn't touch the edges
        var slideRT = MakeRT("SlidingArea", sbRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(0f, -2f));

        var handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(slideRT, false);
        var handleRT       = handleGO.AddComponent<RectTransform>();
        handleRT.anchorMin = Vector2.zero;
        handleRT.anchorMax = Vector2.one;
        handleRT.offsetMin = Vector2.zero;
        handleRT.offsetMax = Vector2.zero;
        var handleImg  = handleGO.AddComponent<Image>();
        handleImg.color = new Color(ColGold.r, ColGold.g, ColGold.b, 0.85f);

        var sb           = sbRT.gameObject.AddComponent<Scrollbar>();
        sb.handleRect    = handleRT;
        sb.targetGraphic = handleImg;
        sb.direction     = Scrollbar.Direction.BottomToTop;

        scroll.verticalScrollbar           = sb;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scroll.verticalScrollbarSpacing    = 2f;
    }

    private void BuildBindRow(RectTransform parent, string label, string action, float yPos, Color rowColor)
    {
        var rowRT   = MakeRT($"Row_{action}", parent,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, yPos), new Vector2(0f, RowH));
        rowRT.gameObject.AddComponent<Image>().color = rowColor;

        // Action label
        MakeText("Label", rowRT,
            new Vector2(0f, 0f), new Vector2(0.55f, 1f), new Vector2(0f, 0.5f),
            new Vector2(Pad, 0f), V2.zero,
            label, 18f, ColParchment, TextAlignmentOptions.MidlineLeft);

        // Bind button
        float btnW = 180f;
        var btnRT  = MakeRT("BindBtn", rowRT,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-Pad, 0f), new Vector2(btnW, RowH - 12f));

        var btnBg    = btnRT.gameObject.AddComponent<Image>();
        btnBg.color  = ColGoldDim;

        var btnText = MakeText("KeyLabel", btnRT,
            V2.zero, V2.one, V2.half, V2.zero, V2.zero,
            SettingsData.KeyLabel(GetCurrentKey(action)),
            18f, ColParchment, TextAlignmentOptions.Center, FontStyles.Bold);

        string capturedAction = action;
        Image  capturedBg     = btnBg;
        TMP_Text capturedText = btnText;

        AddButton(btnRT.gameObject, () => StartRebind(capturedAction, capturedBg, capturedText));
        btnRT.gameObject.AddComponent<UIButtonHover>();
        _bindBtns[action] = (btnBg, btnText);
    }

    private void BuildVolumeContent(RectTransform panel)
    {
        float topY  = -(HeaderH + 2f + TabBarH + 1f);
        float height = PanelH - HeaderH - 2f - TabBarH - 1f - 80f;

        _volumeContent = MakeRT("VolumeContent", panel,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, topY), new Vector2(0f, height)).gameObject;

        var volRT = _volumeContent.GetComponent<RectTransform>();

        // Use top-anchor rows like the controls tab. Each row is 80px tall with 24px gap.
        // Total = 184px. Pad from top = (height - 184) / 2 to vertically centre.
        float rowH2   = 80f;
        float gap     = 24f;
        float totalH  = rowH2 * 2f + gap;
        float startY  = -((height - totalH) * 0.5f);

        BuildVolumeRow(volRT, "Music Volume", SettingsData.MusicVolume, startY,              v => SettingsData.SetMusicVolume(v));
        BuildVolumeRow(volRT, "SFX Volume",   SettingsData.SfxVolume,   startY - rowH2 - gap, v => SettingsData.SetSfxVolume(v));
    }

    private void BuildVolumeRow(RectTransform parent, string labelText, float initialValue,
        float yPos, System.Action<float> onChange)
    {
        const float rowH = 80f;

        // Full-width row anchored from the top — same pattern as controls rows
        var rowRT = MakeRT($"VolumeRow_{labelText}", parent,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, yPos), new Vector2(0f, rowH));
        rowRT.gameObject.AddComponent<Image>().color = ColRow;

        // Label centred at top of row
        MakeText("Label", rowRT,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            V2.zero, new Vector2(0f, 32f),
            labelText, 20f, ColParchment, TextAlignmentOptions.Center, FontStyles.Bold);

        // Slider — fills the lower 36px of the row, with Pad on each side
        var sliderGO = new GameObject("Slider");
        sliderGO.transform.SetParent(rowRT, false);
        var sliderRT              = sliderGO.AddComponent<RectTransform>();
        sliderRT.anchorMin        = new Vector2(0f, 0f);
        sliderRT.anchorMax        = new Vector2(1f, 0f);
        sliderRT.pivot            = new Vector2(0.5f, 0f);
        sliderRT.anchoredPosition = new Vector2(0f, 12f);
        sliderRT.sizeDelta        = new Vector2(-Pad * 2f - 60f, 28f); // leave room for pct label

        var slider          = sliderGO.AddComponent<Slider>();
        slider.minValue     = 0f;
        slider.maxValue     = 1f;
        slider.value        = initialValue;
        slider.wholeNumbers = false;
        slider.direction    = Slider.Direction.LeftToRight;

        // Fill area
        var fillAreaGO       = new GameObject("FillArea");
        fillAreaGO.transform.SetParent(sliderGO.transform, false);
        var fillAreaRT       = fillAreaGO.AddComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRT.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRT.offsetMin = new Vector2(5f, 0f);
        fillAreaRT.offsetMax = new Vector2(-15f, 0f);

        var fillGO       = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        var fillRT       = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = V2.zero;
        fillRT.anchorMax = V2.one;
        fillRT.offsetMin = V2.zero;
        fillRT.offsetMax = V2.zero;
        var fillImg      = fillGO.AddComponent<Image>();
        fillImg.color    = ColGold;
        slider.fillRect  = fillRT;

        // Handle
        var handleAreaGO       = new GameObject("HandleArea");
        handleAreaGO.transform.SetParent(sliderGO.transform, false);
        var handleAreaRT       = handleAreaGO.AddComponent<RectTransform>();
        handleAreaRT.anchorMin = V2.zero;
        handleAreaRT.anchorMax = V2.one;
        handleAreaRT.offsetMin = V2.zero;
        handleAreaRT.offsetMax = V2.zero;

        var handleGO       = new GameObject("Handle");
        handleGO.transform.SetParent(handleAreaGO.transform, false);
        var handleRT       = handleGO.AddComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(22f, 22f);
        var handleImg      = handleGO.AddComponent<Image>();
        handleImg.color    = ColParchment;
        slider.handleRect  = handleRT;

        slider.targetGraphic = handleImg;
        slider.onValueChanged.AddListener(v => onChange(v));

        // Percentage label — inside the row, to the right of the slider
        var pctText = MakeText("Pct", rowRT,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-Pad, 12f), new Vector2(55f, 28f),
            Mathf.RoundToInt(initialValue * 100f) + "%",
            16f, ColGoldDim, TextAlignmentOptions.Center);

        slider.onValueChanged.AddListener(v =>
            pctText.text = Mathf.RoundToInt(v * 100f) + "%");
    }

    private void BuildFooter(RectTransform panel)
    {
        var footerRT = MakeRT("Footer", panel,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
            V2.zero, new Vector2(0f, 80f));
        footerRT.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.07f, 0.03f, 1f);

        // Divider line above footer
        var divRT  = MakeRT("Divider", footerRT,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            V2.zero, new Vector2(0f, 1f));
        divRT.gameObject.AddComponent<Image>().color = ColGoldDim;

        // Button row centred in footer — Resume | Main Menu | Exit
        float totalBtns = BtnW * 3 + 20f * 2;
        float startX    = -totalBtns / 2f + BtnW / 2f;

        // Resume (hidden by default; shown only in dungeon)
        BuildFooterButton("Back", footerRT, new Vector2(startX, 0f),           ColBtnResume, "BACK",          Close);
        BuildFooterButton("MainMenu", footerRT, new Vector2(startX + BtnW + 20f, 0f), ColGoldDim,    "MAIN MENU",       GoToMainMenu);
        BuildFooterButton("Exit",    footerRT, new Vector2(startX + BtnW * 2 + 40f, 0f), ColBtnDanger, "EXIT GAME",       QuitGame);
    }

    private void BuildFooterButton(string name, RectTransform parent, Vector2 pos,
        Color color, string text, UnityEngine.Events.UnityAction onClick)
    {
        var rt      = MakeRT(name, parent,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            pos, new Vector2(BtnW, BtnH));
        var img     = rt.gameObject.AddComponent<Image>();
        img.color   = color;

        MakeText("Label", rt, V2.zero, V2.one, V2.half, V2.zero, V2.zero,
            text, 17f, ColParchment, TextAlignmentOptions.Center, FontStyles.Bold);

        AddButton(rt.gameObject, onClick);
        rt.gameObject.AddComponent<UIButtonHover>();
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void SwitchTab(Tab tab)
    {
        _activeTab = tab;

        _controlsContent.SetActive(tab == Tab.Controls);
        _volumeContent.SetActive(tab == Tab.Volume);

        _tabControlsBg.color   = tab == Tab.Controls ? ColTabActive : ColTabIdle;
        _tabVolumeBg.color     = tab == Tab.Volume   ? ColTabActive : ColTabIdle;
        _tabControlsText.color = tab == Tab.Controls ? ColParchment : new Color(0.7f, 0.62f, 0.45f);
        _tabVolumeText.color   = tab == Tab.Volume   ? ColParchment : new Color(0.7f, 0.62f, 0.45f);

        if (_listeningAction != null) CancelRebind();
    }

    // ── Keybind rebinding ─────────────────────────────────────────────────────

    private void StartRebind(string action, Image btnBg, TMP_Text btnText)
    {
        if (_listeningAction != null) CancelRebind();

        _listeningAction  = action;
        _listeningBtnBg   = btnBg;
        _listeningBtnText = btnText;

        btnBg.color   = ColGold;
        btnText.text  = "...";
        btnText.color = ColDarkBrown;
    }

    private void FinishRebind(KeyCode key)
    {
        if (_listeningBtnBg != null)  _listeningBtnBg.color   = ColGoldDim;
        if (_listeningBtnText != null)
        {
            _listeningBtnText.text  = SettingsData.KeyLabel(key);
            _listeningBtnText.color = ColParchment;
        }

        _listeningAction  = null;
        _listeningBtnBg   = null;
        _listeningBtnText = null;
    }

    private void CancelRebind()
    {
        if (_listeningBtnBg   != null) _listeningBtnBg.color   = ColGoldDim;
        if (_listeningBtnText != null)
        {
            _listeningBtnText.text  = SettingsData.KeyLabel(GetCurrentKey(_listeningAction));
            _listeningBtnText.color = ColParchment;
        }
        _listeningAction  = null;
        _listeningBtnBg   = null;
        _listeningBtnText = null;
    }

    private void RefreshAllBindLabels()
    {
        foreach (var (action, (bg, label)) in _bindBtns)
        {
            bg.color    = ColGoldDim;
            label.text  = SettingsData.KeyLabel(GetCurrentKey(action));
            label.color = ColParchment;
        }
    }

    // ── Navigation buttons ────────────────────────────────────────────────────

    private void GoToMainMenu()
    {
        Close();
        PauseManager.Reset();
        SceneManager.LoadScene("MainMenu");
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static KeyCode GetCurrentKey(string action) => action switch
    {
        "MoveUp"    => SettingsData.MoveUp,
        "MoveDown"  => SettingsData.MoveDown,
        "MoveLeft"  => SettingsData.MoveLeft,
        "MoveRight" => SettingsData.MoveRight,
        "Attack"    => SettingsData.Attack,
        "Slot1"     => SettingsData.Slot1,
        "Slot2"     => SettingsData.Slot2,
        "Slot3"     => SettingsData.Slot3,
        "Dash"      => SettingsData.Dash,
        "ToggleMap" => SettingsData.ToggleMap,
        _           => KeyCode.None,
    };

    private static void AddButton(GameObject go, UnityEngine.Events.UnityAction onClick)
    {
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);
        if (go.TryGetComponent<Image>(out var img)) btn.targetGraphic = img;
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
        if (size == V2.zero)
        {
            rt.offsetMin = V2.zero;
            rt.offsetMax = V2.zero;
        }
        var tmp              = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font             = font;
        tmp.text             = text;
        tmp.fontSize         = fontSize;
        tmp.color            = color;
        tmp.alignment        = align;
        tmp.fontStyle        = style;
        tmp.textWrappingMode        = TMPro.TextWrappingModes.NoWrap;
        tmp.overflowMode     = TextOverflowModes.Overflow;
        tmp.raycastTarget    = false;
        return tmp;
    }

    // Shorthand Vector2 constants
    private static class V2
    {
        public static Vector2 zero => Vector2.zero;
        public static Vector2 one  => Vector2.one;
        public static Vector2 half => new Vector2(0.5f, 0.5f);
    }
}
