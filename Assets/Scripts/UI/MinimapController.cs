using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Procedural minimap — always-visible 150×75 corner widget with an expandable
/// landscape scroll panel.
///
/// How it works:
///   • When FloorAssembler fires OnFloorReady, this samples every Wall and Ground
///     Tilemap in the scene to build a static Texture2D of the floor layout.
///   • A second Texture2D tracks per-chamber fog: each 20×20 chamber is revealed
///     (fog cleared) the first time the player enters it.
///   • Enemy and player positions are tracked as pooled RawImage "dots" repositioned
///     each frame.  Enemy dots are always visible even through fog.
///   • Clicking the corner widget or pressing SettingsData.ToggleMap (default M)
///     opens/closes the expanded view, which pauses the game.
///
/// Setup:
///   1. Add this component to any persistent GameObject in the Dungeon scene
///      (e.g. a "Managers" object).
///   2. Optionally assign the Alagard TMP font asset so the expanded view shows
///      the "FLOOR MAP" title and key-hint text.
/// </summary>
public class MinimapController : MonoBehaviour
{
    [Header("Font (optional — for expanded view text)")]
    [SerializeField] private TMP_FontAsset font;

    // ── Map geometry ──────────────────────────────────────────────────────────
    private const int   MapW       = 80;          // world units wide
    private const int   MapH       = 40;          // world units tall
    private const int   ChamberW   = 20;          // world units per chamber side
    private const int   PixPerUnit = 2;           // texture pixels per world unit
    private const int   TexW       = MapW * PixPerUnit;   // 160
    private const int   TexH       = MapH * PixPerUnit;   // 80

    // ── Corner widget ─────────────────────────────────────────────────────────
    private const float MiniW = 400f;
    private const float MiniH = 200f;

    // ── Expanded panel map display ────────────────────────────────────────────
    private const float ExpandW = 880f;
    private const float ExpandH = 440f;

    // ── Colors ────────────────────────────────────────────────────────────────
    private static readonly Color ColFloor  = new Color(0.28f, 0.20f, 0.12f, 0.95f);
    private static readonly Color ColWall   = new Color(0.10f, 0.07f, 0.04f, 1.00f);
    private static readonly Color ColEdge   = new Color(0.50f, 0.36f, 0.18f, 1.00f);
    private static readonly Color ColFog    = new Color(0.05f, 0.03f, 0.02f, 1.00f);
    private static readonly Color ColEnemy  = new Color(1.00f, 0.10f, 0.05f, 1.00f);
    private static readonly Color ColPlayer = new Color(1.00f, 0.88f, 0.35f, 1.00f);

    /// <summary>RectTransform of the corner minimap widget.</summary>
    public RectTransform MinimapRT => _miniRT;
    /// <summary>The canvas GO for sorting order manipulation.</summary>
    public GameObject CanvasGO => _canvasGO;

    // ── Runtime state ─────────────────────────────────────────────────────────
    private Texture2D _mapTex;
    private Texture2D _fogTex;
    private bool[]    _revealed = new bool[8];
    private Vector3   _mapOrigin;
    private bool      _expanded;

    // ── Canvas / UI nodes ─────────────────────────────────────────────────────
    private GameObject    _canvasGO;
    private RectTransform _miniRT;
    private RectTransform _expandRT;
    private RawImage      _miniMapImg,    _expandMapImg;
    private RawImage      _miniFogImg,    _expandFogImg;
    private RectTransform _miniDotsRoot,  _expandDotsRoot;
    private TMP_Text      _keyHintText;

    // ── Player ────────────────────────────────────────────────────────────────
    private Transform _player;

    // ── Fog fade ──────────────────────────────────────────────────────────────
    private struct FadingChamber { public int col, row; public float alpha; }
    private readonly List<FadingChamber> _fadingChambers = new List<FadingChamber>();
    private const float FogFadeDuration = 1.2f;

    // ── Dot pool ──────────────────────────────────────────────────────────────
    private struct DotPair
    {
        public RectTransform mini;
        public RectTransform expand;
    }
    private readonly List<DotPair> _dotPool = new List<DotPair>();
    private static Texture2D _dotTex;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        BuildCanvas();
        _canvasGO.SetActive(false); // hidden until floor assembles
        FloorAssembler.OnFloorReady += HandleFloorReady;
    }

    private void Start()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) _player = playerObj.transform;
        SettingsData.OnBindingsChanged += RefreshKeyHint;
    }

    private void OnDestroy()
    {
        FloorAssembler.OnFloorReady    -= HandleFloorReady;
        SettingsData.OnBindingsChanged -= RefreshKeyHint;
        if (_mapTex   != null) Destroy(_mapTex);
        if (_fogTex   != null) Destroy(_fogTex);
        if (_canvasGO != null) Destroy(_canvasGO);
    }

    private void Update()
    {
        if (_mapTex == null) return; // floor not yet assembled

        if (Input.GetKeyDown(SettingsData.ToggleMap))
            Toggle();

        UpdateFog();
        UpdateFadingChambers();
        UpdateDots();
    }

    // ── Floor ready ───────────────────────────────────────────────────────────

    private void HandleFloorReady(Vector3 origin)
    {
        _mapOrigin = origin;
        _revealed  = new bool[8];

        BuildMapTexture();
        BuildFogTexture();

        _miniMapImg.texture   = _mapTex;
        _miniFogImg.texture   = _fogTex;
        _expandMapImg.texture = _mapTex;
        _expandFogImg.texture = _fogTex;

        // Re-find the player in case the scene reloaded
        if (_player == null)
        {
            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) _player = playerObj.transform;
        }

        _canvasGO.SetActive(true);

        // If the expanded panel was open across a floor transition, close it
        if (_expanded) Toggle();
    }

    // ── Map texture ────────────────────────────────────────────────────────────

    private void BuildMapTexture()
    {
        if (_mapTex != null) Destroy(_mapTex);
        _mapTex            = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
        _mapTex.filterMode = FilterMode.Point;

        var pixels = new Color[TexW * TexH]; // default Color is (0,0,0,0) — transparent

        // Ground tiles (incl. Details layer which also uses "Ground" layer)
        int groundLayer = LayerMask.NameToLayer("Ground");
        foreach (var tm in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            if (tm.gameObject.layer == groundLayer)
                PaintTilemap(tm, pixels, ColFloor, null);
        }

        // Wall tiles — paint dark and record pixel positions for outline pass
        var isWall   = new bool[TexW * TexH];
        int wallLayer = LayerMask.NameToLayer("Walls");
        foreach (var tm in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            if (tm.gameObject.layer == wallLayer)
                PaintTilemap(tm, pixels, ColWall, isWall);
        }

        // Outline: wall pixels that border a visible floor pixel get a lighter edge
        for (int y = 1; y < TexH - 1; y++)
        for (int x = 1; x < TexW - 1; x++)
        {
            if (!isWall[y * TexW + x]) continue;
            bool bordersFloor =
                (pixels[(y + 1) * TexW + x    ].a > 0f && !isWall[(y + 1) * TexW + x    ]) ||
                (pixels[(y - 1) * TexW + x    ].a > 0f && !isWall[(y - 1) * TexW + x    ]) ||
                (pixels[y       * TexW + x + 1].a > 0f && !isWall[y       * TexW + x + 1]) ||
                (pixels[y       * TexW + x - 1].a > 0f && !isWall[y       * TexW + x - 1]);
            if (bordersFloor)
                pixels[y * TexW + x] = ColEdge;
        }

        _mapTex.SetPixels(pixels);
        _mapTex.Apply();
    }

    private void PaintTilemap(Tilemap tm, Color[] pixels, Color color, bool[] wallMask)
    {
        var bounds = tm.cellBounds;
        for (int cy = bounds.yMin; cy < bounds.yMax; cy++)
        for (int cx = bounds.xMin; cx < bounds.xMax; cx++)
        {
            var cell = new Vector3Int(cx, cy, 0);
            if (!tm.HasTile(cell)) continue;

            // CellToWorld returns the bottom-left corner of the cell; center it
            Vector3 world = tm.CellToWorld(cell) + new Vector3(0.5f, 0.5f, 0f);
            int px = Mathf.FloorToInt((world.x - _mapOrigin.x) * PixPerUnit);
            int py = Mathf.FloorToInt((world.y - _mapOrigin.y) * PixPerUnit);

            for (int dy = 0; dy < PixPerUnit; dy++)
            for (int dx = 0; dx < PixPerUnit; dx++)
            {
                int tx = px + dx;
                int ty = py + dy;
                if (tx < 0 || tx >= TexW || ty < 0 || ty >= TexH) continue;
                int idx = ty * TexW + tx;
                pixels[idx] = color;
                if (wallMask != null) wallMask[idx] = true;
            }
        }
    }

    // ── Fog texture ────────────────────────────────────────────────────────────

    private void BuildFogTexture()
    {
        if (_fogTex != null) Destroy(_fogTex);

        // 2 texels per chamber (8 columns × 4 rows).
        // Each chamber occupies a 2×2 block of texels. Point filtering gives
        // hard per-chamber boundaries with no bleed into adjacent chambers.
        // The fade animation (FadingChamber) provides smooth reveal instead.
        _fogTex            = new Texture2D(8, 4, TextureFormat.RGBA32, false);
        _fogTex.filterMode = FilterMode.Point;
        _fogTex.wrapMode   = TextureWrapMode.Clamp;

        var pixels = new Color[32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = ColFog;
        _fogTex.SetPixels(pixels);
        _fogTex.Apply();
    }

    private void UpdateFog()
    {
        if (_player == null) return;

        float rx = _player.position.x - _mapOrigin.x;
        float ry = _player.position.y - _mapOrigin.y;
        int   col = Mathf.Clamp(Mathf.FloorToInt(rx / ChamberW), 0, 3);
        int   row = Mathf.Clamp(Mathf.FloorToInt(ry / ChamberW), 0, 1);
        int   idx = row * 4 + col;

        if (!_revealed[idx])
        {
            _revealed[idx] = true;
            RevealChamber(col, row);
        }
    }

    private void RevealChamber(int col, int row)
    {
        // Start a fade from fully opaque fog to clear.
        _fadingChambers.Add(new FadingChamber { col = col, row = row, alpha = 1f });
    }

    private void UpdateFadingChambers()
    {
        if (_fadingChambers.Count == 0) return;
        bool dirty = false;
        for (int i = _fadingChambers.Count - 1; i >= 0; i--)
        {
            var fc = _fadingChambers[i];
            fc.alpha -= Time.deltaTime / FogFadeDuration;
            dirty = true;
            if (fc.alpha <= 0f)
            {
                SetChamberFogAlpha(fc.col, fc.row, 0f);
                _fadingChambers.RemoveAt(i);
            }
            else
            {
                SetChamberFogAlpha(fc.col, fc.row, fc.alpha);
                _fadingChambers[i] = fc;
            }
        }
        if (dirty) _fogTex.Apply();
    }

    private void SetChamberFogAlpha(int col, int row, float alpha)
    {
        Color c = new Color(ColFog.r, ColFog.g, ColFog.b, alpha);
        int tx = col * 2, ty = row * 2;
        _fogTex.SetPixel(tx,     ty,     c);
        _fogTex.SetPixel(tx + 1, ty,     c);
        _fogTex.SetPixel(tx,     ty + 1, c);
        _fogTex.SetPixel(tx + 1, ty + 1, c);
    }

    // ── Dot pool ──────────────────────────────────────────────────────────────

    private void UpdateDots()
    {
        var enemies = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);

        int liveCount = 0;
        foreach (var e in enemies) if (!e.IsDead) liveCount++;

        // Grow pool if needed (slot 0 = player, then enemies)
        while (_dotPool.Count < liveCount + 1)
            _dotPool.Add(new DotPair
            {
                mini   = MakeDotRT(_miniDotsRoot),
                expand = MakeDotRT(_expandDotsRoot),
            });

        // Deactivate all first, then re-activate only those we fill
        for (int i = 0; i < _dotPool.Count; i++)
        {
            _dotPool[i].mini.gameObject.SetActive(false);
            _dotPool[i].expand.gameObject.SetActive(false);
        }

        // Player dot (always slot 0)
        if (_player != null)
        {
            var uv = WorldToUV(_player.position);
            PlaceDot(_dotPool[0].mini,   uv, MiniW,   MiniH,   20f, ColPlayer);
            PlaceDot(_dotPool[0].expand, uv, ExpandW, ExpandH, 40f, ColPlayer);
            _dotPool[0].mini.gameObject.SetActive(true);
            _dotPool[0].expand.gameObject.SetActive(_expanded);
        }

        // Enemy dots — always visible (even through fog)
        int slot = 1;
        foreach (var e in enemies)
        {
            if (e.IsDead) continue;
            var uv = WorldToUV(e.transform.position);
            PlaceDot(_dotPool[slot].mini,   uv, MiniW,   MiniH,   16f, ColEnemy);
            PlaceDot(_dotPool[slot].expand, uv, ExpandW, ExpandH, 32f, ColEnemy);
            _dotPool[slot].mini.gameObject.SetActive(true);
            _dotPool[slot].expand.gameObject.SetActive(_expanded);
            slot++;
        }
    }

    private Vector2 WorldToUV(Vector3 world) => new Vector2(
        Mathf.Clamp01((world.x - _mapOrigin.x) / MapW),
        Mathf.Clamp01((world.y - _mapOrigin.y) / MapH));

    private static void PlaceDot(RectTransform rt, Vector2 uv,
        float containerW, float containerH, float size, Color color)
    {
        rt.anchoredPosition = new Vector2(uv.x * containerW, uv.y * containerH);
        rt.sizeDelta        = new Vector2(size, size);
        var img = rt.GetComponent<RawImage>();
        if (img != null) img.color = color;
    }

    private static RectTransform MakeDotRT(RectTransform parent)
    {
        var go             = new GameObject("Dot");
        go.transform.SetParent(parent, false);
        var rt             = go.AddComponent<RectTransform>();
        rt.anchorMin       = Vector2.zero;
        rt.anchorMax       = Vector2.zero;
        rt.pivot           = new Vector2(0.5f, 0.5f);
        var img            = go.AddComponent<RawImage>();
        img.texture        = GetDotTex();
        img.raycastTarget  = false;
        go.SetActive(false);
        return rt;
    }

    // Soft-circle glow texture for dots — generated once, shared by all dots.
    private static Texture2D GetDotTex()
    {
        if (_dotTex != null) return _dotTex;
        const int s = 16;
        _dotTex            = new Texture2D(s, s, TextureFormat.RGBA32, false);
        _dotTex.filterMode = FilterMode.Bilinear;
        float r = (s - 1) * 0.5f;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
            float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2f); // quadratic falloff
            _dotTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        _dotTex.Apply();
        return _dotTex;
    }

    // ── Toggle ────────────────────────────────────────────────────────────────

    /// <summary>Whether the minimap is currently expanded.</summary>
    public bool IsExpanded => _expanded;

    public void Toggle()
    {
        _expanded = !_expanded;
        _expandRT.gameObject.SetActive(_expanded);
        if (_expanded) PauseManager.Pause();
        else           PauseManager.Unpause();
        RefreshKeyHint();
    }

    private void RefreshKeyHint()
    {
        if (_keyHintText == null) return;
        _keyHintText.text = $"[{SettingsData.KeyLabel(SettingsData.ToggleMap)}] to close";
    }

    // ── Canvas construction ────────────────────────────────────────────────────

    private void BuildCanvas()
    {
        _canvasGO = new GameObject("Minimap_Canvas");
        var canvas          = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150; // above HUD (100), below stage transition (200)

        var scaler                 = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _canvasGO.AddComponent<GraphicRaycaster>();

        BuildMiniView();
        BuildExpandedView();
    }

    // ── Corner widget ─────────────────────────────────────────────────────────

    private void BuildMiniView()
    {
        // Top-left corner with standard margin — heart bar is now at the bottom.
        _miniRT = MakeRT("MinimapCorner", _canvasGO.transform,
            Vector2.up, Vector2.up, new Vector2(0f, 1f),
            new Vector2(20f, -20f), new Vector2(MiniW, MiniH));

        // Rounded-rect background — sprite defines the corner shape.
        // Mask clips all children (map, fog, dots, frame) to the rounded outline.
        var bg      = _miniRT.gameObject.AddComponent<Image>();
        bg.sprite   = CreateRoundedRectSprite(100, 50, 2);
        bg.type     = Image.Type.Simple;
        bg.color    = new Color(0.10f, 0.07f, 0.04f, 0.92f);
        var mask    = _miniRT.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        // Map layer — inset 2px from edges
        var mapRT         = MakeRT("MapLayer", _miniRT,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
        mapRT.offsetMin   = new Vector2(2f, 2f);
        mapRT.offsetMax   = new Vector2(-2f, -2f);
        _miniMapImg       = mapRT.gameObject.AddComponent<RawImage>();
        _miniMapImg.raycastTarget = false;

        // Fog layer — same inset
        var fogRT         = MakeRT("FogLayer", _miniRT,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
        fogRT.offsetMin   = new Vector2(2f, 2f);
        fogRT.offsetMax   = new Vector2(-2f, -2f);
        _miniFogImg       = fogRT.gameObject.AddComponent<RawImage>();
        _miniFogImg.raycastTarget = false;

        // Dots container — anchored at bottom-left, sized to match map area.
        // Dot anchoredPositions use (uv * MiniW, uv * MiniH) so origin = bottom-left.
        _miniDotsRoot = MakeRT("Dots", _miniRT,
            Vector2.zero, Vector2.zero, Vector2.zero,
            new Vector2(2f, 2f), new Vector2(MiniW - 4f, MiniH - 4f));

        // Gold border frame lines
        AddFrameLines(_miniRT, new Color(0.60f, 0.42f, 0.15f, 0.85f), 1.5f);

        // Invisible click-to-expand button — must sit above map/fog/dots layers
        var clickRT        = MakeRT("ClickArea", _miniRT,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
        clickRT.offsetMin  = Vector2.zero;
        clickRT.offsetMax  = Vector2.zero;
        var clickImg       = clickRT.gameObject.AddComponent<Image>();
        clickImg.color     = Color.clear;
        var btn            = clickRT.gameObject.AddComponent<Button>();
        btn.targetGraphic  = clickImg;
        btn.onClick.AddListener(Toggle);
    }

    // ── Expanded scroll panel ─────────────────────────────────────────────────

    private void BuildExpandedView()
    {
        // Full-screen dark overlay (click to close)
        _expandRT = MakeRT("ExpandedOverlay", _canvasGO.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        _expandRT.offsetMin = Vector2.zero;
        _expandRT.offsetMax = Vector2.zero;
        var overlayImg      = _expandRT.gameObject.AddComponent<Image>();
        overlayImg.color    = new Color(0f, 0f, 0f, 0.72f);
        var overlayBtn      = _expandRT.gameObject.AddComponent<Button>();
        overlayBtn.targetGraphic = overlayImg;
        overlayBtn.onClick.AddListener(Toggle);

        // Landscape parchment panel (~72% × 52% of reference 1920×1080)
        const float panelW = 1920f * 0.72f;
        const float panelH = 1080f * 0.52f;
        var panelRT        = MakeRT("ScrollPanel", _expandRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(panelW, panelH));
        var panelBg        = panelRT.gameObject.AddComponent<Image>();
        panelBg.color      = new Color(0.88f, 0.80f, 0.62f, 1f);

        // Absorb clicks inside panel so they don't fall through to the overlay close
        var panelBtn       = panelRT.gameObject.AddComponent<Button>();
        panelBtn.targetGraphic = panelBg;
        panelBtn.onClick.AddListener(() => { }); // no-op

        // X close button — top-right of panel
        float closeBtnSize = 40f;
        var closeBtnRT = MakeRT("CloseBtn", panelRT,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-8f, -8f), new Vector2(closeBtnSize, closeBtnSize));
        var closeBtnImg = closeBtnRT.gameObject.AddComponent<Image>();
        closeBtnImg.color = new Color(0.55f, 0.12f, 0.08f, 0.9f);
        var closeBtnButton = closeBtnRT.gameObject.AddComponent<Button>();
        closeBtnButton.targetGraphic = closeBtnImg;
        closeBtnButton.onClick.AddListener(Toggle);
        closeBtnRT.gameObject.AddComponent<UIButtonHover>();
        if (font != null)
        {
            var labelRT = MakeRT("CloseBtnLabel", closeBtnRT,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            var closeLabel = labelRT.gameObject.AddComponent<TextMeshProUGUI>();
            closeLabel.font = font;
            closeLabel.text = "X";
            closeLabel.fontSize = 22f;
            closeLabel.fontStyle = FontStyles.Bold;
            closeLabel.alignment = TextAlignmentOptions.Center;
            closeLabel.color = new Color(0.93f, 0.87f, 0.72f);
            closeLabel.raycastTarget = false;
        }

        // Dark inner panel with 18px inset on all sides
        var innerRT        = MakeRT("InnerPanel", panelRT,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
        innerRT.offsetMin  = new Vector2(18f, 18f);
        innerRT.offsetMax  = new Vector2(-18f, -18f);
        var innerBg        = innerRT.gameObject.AddComponent<Image>();
        innerBg.color      = new Color(0.14f, 0.10f, 0.06f, 0.92f);

        // "FLOOR MAP" title at top of inner panel
        if (font != null)
        {
            var titleRT        = MakeRT("Title", innerRT,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(400f, 36f));
            var titleText      = titleRT.gameObject.AddComponent<TextMeshProUGUI>();
            titleText.font     = font;
            titleText.text     = "FLOOR MAP";
            titleText.fontSize = 22f;
            titleText.fontStyle    = FontStyles.Bold;
            titleText.alignment    = TextAlignmentOptions.Center;
            titleText.color        = new Color(0.85f, 0.65f, 0.15f, 1f);
            titleText.raycastTarget = false;
        }

        // Map display — centered, slightly above center to leave room for key hint
        var mapRT          = MakeRT("MapLayer", innerRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 8f), new Vector2(ExpandW, ExpandH));
        _expandMapImg      = mapRT.gameObject.AddComponent<RawImage>();
        _expandMapImg.raycastTarget = false;

        // Fog layer — same position and size as map
        var fogRT          = MakeRT("FogLayer", innerRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 8f), new Vector2(ExpandW, ExpandH));
        _expandFogImg      = fogRT.gameObject.AddComponent<RawImage>();
        _expandFogImg.raycastTarget = false;

        // Dots container — bottom-left of map, same size.
        // Dot positions: (uv.x * ExpandW, uv.y * ExpandH) relative to pivot (0,0).
        _expandDotsRoot    = MakeRT("Dots", innerRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(-ExpandW * 0.5f, 8f - ExpandH * 0.5f),
            new Vector2(ExpandW, ExpandH));

        // Key hint at bottom of inner panel
        if (font != null)
        {
            var hintRT         = MakeRT("KeyHint", innerRT,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 12f), new Vector2(300f, 28f));
            _keyHintText       = hintRT.gameObject.AddComponent<TextMeshProUGUI>();
            _keyHintText.font  = font;
            _keyHintText.fontSize  = 16f;
            _keyHintText.alignment = TextAlignmentOptions.Center;
            _keyHintText.color     = new Color(0.55f, 0.45f, 0.30f, 0.85f);
            _keyHintText.raycastTarget = false;
            RefreshKeyHint();
        }

        _expandRT.gameObject.SetActive(false);
    }

    // ── Frame lines ────────────────────────────────────────────────────────────

    private static void AddFrameLines(RectTransform parent, Color color, float t)
    {
        // Top
        var top        = MakeRT("Frame_T", parent,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            Vector2.zero, new Vector2(0f, t));
        top.gameObject.AddComponent<Image>().color = color;

        // Bottom
        var bot        = MakeRT("Frame_B", parent,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
            Vector2.zero, new Vector2(0f, t));
        bot.gameObject.AddComponent<Image>().color = color;

        // Left
        var left       = MakeRT("Frame_L", parent,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f),
            Vector2.zero, new Vector2(t, 0f));
        left.gameObject.AddComponent<Image>().color = color;

        // Right
        var right      = MakeRT("Frame_R", parent,
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            Vector2.zero, new Vector2(t, 0f));
        right.gameObject.AddComponent<Image>().color = color;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RectTransform MakeRT(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go             = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt             = go.AddComponent<RectTransform>();
        rt.anchorMin       = anchorMin;
        rt.anchorMax       = anchorMax;
        rt.pivot           = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta       = size;
        return rt;
    }

    /// <summary>
    /// Generates a white rounded-rectangle Texture2D and wraps it as a Sprite
    /// with 9-slice borders equal to the corner radius. Apply with Image.Type.Sliced
    /// so it stretches cleanly to any size while keeping crisp rounded corners.
    /// </summary>
    private static Sprite CreateRoundedRectSprite(int w, int h, int radius)
    {
        var tex            = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode     = FilterMode.Bilinear;
        var pixels         = new Color[w * h];

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // Nearest point on the inset rectangle (corner centres)
            float cx   = Mathf.Clamp(x, radius, w - 1 - radius);
            float cy   = Mathf.Clamp(y, radius, h - 1 - radius);
            float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            // +0.5 gives a one-pixel anti-aliased edge
            float a    = Mathf.Clamp01(radius - dist + 0.5f);
            pixels[y * w + x] = new Color(1f, 1f, 1f, a);
        }

        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(
            tex,
            new Rect(0, 0, w, h),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit: 1f,
            extrude: 0,
            meshType: SpriteMeshType.FullRect,
            border: new Vector4(radius, radius, radius, radius));
    }
}
