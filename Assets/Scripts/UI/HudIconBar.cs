using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// HUD icons: Grimoire (G) + Merge (M) top-left beside heart; Settings (S) top-right.
/// Clicking each toggles the corresponding overlay UI.
/// Merge button only glows at milestone stages (5, 10, 15...).
///
/// Setup: Create empty GameObject, attach this script, assign sprites + font.
///        Wire grimoireUI / mergeRitualUI references after those scripts exist.
/// </summary>
public class HudIconBar : MonoBehaviour
{
    [Header("Icons")]
    [SerializeField] private Sprite grimoireIcon;   // or null — falls back to text "G"
    [SerializeField] private Sprite mergeIcon;      // or null — falls back to text "M"
    [SerializeField] private Sprite settingsIcon;   // settingsIcon.png

    [Header("Font")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Overlay References (wire after creating those scripts)")]
    [SerializeField] private GrimoireUI grimoireUI;
    [SerializeField] private MergeRitualUI mergeRitualUI;
    [SerializeField] private SettingsUI settingsUI;

    [Header("Stage Tracking")]
    [SerializeField] private int currentStage = 1;

    private const float IconSize = 64f;
    private const float IconGap  = 10f;
    private const float Margin   = 20f;

    private GameObject canvasGO;
    private Image mergeImage;
    private float glowPhase;

    // Enemy counter
    private TMP_Text enemyCountText;
    private RectTransform _enemyCounterRT;
    private FloorClearDetector clearDetector;

    // Page display (top-center)
    private TMP_Text _pageText;
    private RectTransform _pageRT;

    // Grimoire/Merge button RTs (for tutorial spotlight)
    private RectTransform _grimoireButtonRT;
    private RectTransform _mergeButtonRT;

    /// <summary>RectTransform of the Grimoire icon button.</summary>
    public RectTransform GrimoireButtonRT => _grimoireButtonRT;
    /// <summary>RectTransform of the Merge icon button.</summary>
    public RectTransform MergeButtonRT => _mergeButtonRT;
    /// <summary>RectTransform of the enemy counter pill.</summary>
    public RectTransform EnemyCounterRT => _enemyCounterRT;
    /// <summary>RectTransform of the page display.</summary>
    public RectTransform PageDisplayRT => _pageRT;
    /// <summary>The canvas GO for sorting order manipulation.</summary>
    public GameObject CanvasGO => canvasGO;

    private void Start()
    {
        // Auto-find overlay UIs if not wired in Inspector
        if (grimoireUI == null)
            grimoireUI = FindAnyObjectByType<GrimoireUI>();
        if (mergeRitualUI == null)
            mergeRitualUI = FindAnyObjectByType<MergeRitualUI>();
        if (settingsUI == null)
            settingsUI = FindAnyObjectByType<SettingsUI>();

        clearDetector = FindAnyObjectByType<FloorClearDetector>();
        if (clearDetector != null)
            clearDetector.OnEnemyCountChanged += OnEnemyCountChanged;

        BuildBar();
    }

    private void OnDestroy()
    {
        if (clearDetector != null)
            clearDetector.OnEnemyCountChanged -= OnEnemyCountChanged;
        if (canvasGO != null) Destroy(canvasGO);
    }

    private void Update()
    {
        // Merge glow at milestone stages
        if (mergeImage != null && IsMergeStage())
        {
            glowPhase += Time.unscaledDeltaTime * 2f;
            float t = (Mathf.Sin(glowPhase * Mathf.PI) + 1f) / 2f;
            mergeImage.color = Color.Lerp(Color.white, new Color(0.8f, 0.5f, 1f), t);
        }

        // Keyboard shortcuts (unscaled so they work while paused)
        if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.G))
        {
            if (LockedTo == null || LockedTo == "grimoire") ToggleGrimoire();
        }
        // M key is reserved for minimap (handled by MinimapController)
    }

    /// <summary>When set, only the specified icon is clickable/keyboard-accessible. null = all enabled.</summary>
    public string LockedTo { get; set; }

    public void SetStage(int stage) { currentStage = stage; UpdatePageDisplay(); }
    public bool IsMergeStage() => currentStage >= 5 && currentStage % 5 == 0;

    // ── Build ────────────────────────────────────────────────────────────────

    private void BuildBar()
    {
        // Ensure an EventSystem exists so button clicks are dispatched
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();
        }

        canvasGO = new GameObject("HudIconBar_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 91;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Top-right bar: Grimoire | Merge | Settings (left → right) ───────
        float tripleBarW = IconSize * 3f + IconGap * 2f + 12f;
        var rightBarRT = MakeRT("TopRightBar", canvasGO.transform,
            Vector2.one, Vector2.one, Vector2.one,
            new Vector2(-Margin, -Margin),
            new Vector2(tripleBarW, IconSize + 10f));

        var grimoireImg = MakeIconButtonLTR("Grimoire", rightBarRT, 0f,                        grimoireIcon, "G", OnGrimoireClick);
        _grimoireButtonRT = grimoireImg.rectTransform;
        mergeImage = MakeIconButtonLTR("Merge",   rightBarRT, IconSize + IconGap,          mergeIcon,    "M", OnMergeClick);
        _mergeButtonRT = mergeImage.rectTransform;
        MakeIconButtonLTR("Settings", rightBarRT, (IconSize + IconGap) * 2f, settingsIcon, "S", OnSettingsClick);

        // ── Enemy counter ───────────────────────────────────────────────────
        BuildEnemyCounter();

        // ── Top-center page display ─────────────────────────────────────────
        BuildPageDisplay();
    }

    private void BuildEnemyCounter()
    {
        _enemyCounterRT = MakeRT("EnemyCounter", canvasGO.transform,
            Vector2.right, Vector2.right, Vector2.right,
            new Vector2(-Margin, Margin), new Vector2(200f, 34f));
        var counterRT = _enemyCounterRT;

        // Background pill
        var bgImg = counterRT.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.45f);

        // Text
        var textRT = MakeRT("Text", counterRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        enemyCountText = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        enemyCountText.font = font;
        enemyCountText.fontSize = 20f;
        enemyCountText.fontStyle = FontStyles.Bold;
        enemyCountText.alignment = TextAlignmentOptions.Center;
        enemyCountText.color = Color.white;
        enemyCountText.raycastTarget = false;

        UpdateEnemyCountDisplay();
    }

    private void BuildPageDisplay()
    {
        _pageRT = MakeRT("PageDisplay", canvasGO.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -Margin),
            new Vector2(200f, 38f));

        // No background
        var bgImg = _pageRT.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0f);

        var textRT = MakeRT("Text", _pageRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        _pageText = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        _pageText.font = font;
        _pageText.fontSize = 28f;
        _pageText.fontStyle = FontStyles.Bold;
        _pageText.alignment = TextAlignmentOptions.Center;
        _pageText.color = new Color(0.93f, 0.87f, 0.72f);
        _pageText.raycastTarget = false;

        UpdatePageDisplay();
    }

    private void UpdatePageDisplay()
    {
        if (_pageText != null)
            _pageText.text = $"Page {currentStage}";
    }

    private Image MakeIconButton(string name, RectTransform parent, float xPos,
        Sprite icon, string fallbackLetter, UnityEngine.Events.UnityAction onClick)
    {
        var rt = MakeRT(name, parent,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(xPos, 0f), new Vector2(IconSize, IconSize));

        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white;

        if (icon != null)
        {
            img.sprite = icon;
            img.preserveAspect = true;
        }
        else
        {
            img.color = new Color(0.96f, 0.9f, 0.78f);
        }

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        rt.gameObject.AddComponent<UIButtonHover>();

        // Fallback letter if no sprite
        if (icon == null && font != null)
        {
            var textRT = MakeRT("Label", rt,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;
            var tmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.text = fallbackLetter;
            tmp.fontSize = 28f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.29f, 0.22f, 0.16f);
            tmp.raycastTarget = false;
        }

        return img;
    }

    // Left-anchored variant of MakeIconButton for the top-left bar.
    private Image MakeIconButtonLTR(string name, RectTransform parent, float xPos,
        Sprite icon, string fallbackLetter, UnityEngine.Events.UnityAction onClick)
    {
        var rt = MakeRT(name, parent,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(xPos, 0f), new Vector2(IconSize, IconSize));

        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white;

        if (icon != null)
        {
            img.sprite = icon;
            img.preserveAspect = true;
        }
        else
        {
            img.color = new Color(0.96f, 0.9f, 0.78f);
        }

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        rt.gameObject.AddComponent<UIButtonHover>();

        if (icon == null && font != null)
        {
            var textRT = MakeRT("Label", rt,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;
            var tmp = textRT.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.text = fallbackLetter;
            tmp.fontSize = 28f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.29f, 0.22f, 0.16f);
            tmp.raycastTarget = false;
        }

        return img;
    }

    // ── Button handlers ──────────────────────────────────────────────────────

    private void OnGrimoireClick()
    {
        if (LockedTo != null && LockedTo != "grimoire") return;
        ToggleGrimoire();
    }
    private void OnMergeClick()
    {
        if (LockedTo != null && LockedTo != "merge") return;
        ToggleMerge();
    }
    private void OnSettingsClick()
    {
        if (LockedTo != null && LockedTo != "settings") return;
        if (SettingsUI.Instance != null) SettingsUI.Instance.Toggle(true);
    }

    private void ToggleGrimoire()
    {
        if (grimoireUI != null) grimoireUI.Toggle();
    }

    private void ToggleMerge()
    {
        if (mergeRitualUI != null) mergeRitualUI.Toggle();
    }

    // ── Enemy counter ────────────────────────────────────────────────────────

    private void OnEnemyCountChanged(int alive, int total)
    {
        UpdateEnemyCountDisplay();
    }

    private void UpdateEnemyCountDisplay()
    {
        if (enemyCountText == null) return;

        if (clearDetector == null || clearDetector.TotalEnemies == 0)
        {
            enemyCountText.text = "";
            return;
        }

        int alive = clearDetector.AliveEnemies;
        int total = clearDetector.TotalEnemies;

        if (alive <= 0)
            enemyCountText.text = "<color=#66CC77>CLEARED</color>";
        else
            enemyCountText.text = $"Enemies: {alive} / {total}";
    }

    /// <summary>Call when a new floor starts to refresh the counter.</summary>
    public void RefreshEnemyCount() => UpdateEnemyCountDisplay();

    // ── Helpers ──────────────────────────────────────────────────────────────

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
