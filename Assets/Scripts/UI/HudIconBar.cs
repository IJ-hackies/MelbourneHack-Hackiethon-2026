using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Top-right HUD icons: Grimoire (G), Merge (M), Settings (S).
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

    [Header("Stage Tracking")]
    [SerializeField] private int currentStage = 1;

    private const float IconSize = 64f;
    private const float IconGap  = 10f;
    private const float Margin   = 20f;

    private GameObject canvasGO;
    private Image mergeImage;
    private float glowPhase;

    private void Start()
    {
        // Auto-find overlay UIs if not wired in Inspector
        if (grimoireUI == null)
            grimoireUI = FindAnyObjectByType<GrimoireUI>();
        if (mergeRitualUI == null)
            mergeRitualUI = FindAnyObjectByType<MergeRitualUI>();

        BuildBar();
    }

    private void OnDestroy()
    {
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
            ToggleGrimoire();
        if (Input.GetKeyDown(KeyCode.M) && IsMergeStage())
            ToggleMerge();
    }

    public void SetStage(int stage) => currentStage = stage;
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

        // Bar (no background — transparent container for icons)
        float totalWidth = IconSize * 3 + IconGap * 2 + 12f;
        var barRT = MakeRT("Bar", canvasGO.transform,
            Vector2.one, Vector2.one, Vector2.one,
            new Vector2(-Margin, -Margin),
            new Vector2(totalWidth, IconSize + 10f));

        // Icons: right-to-left from top-right
        float x = -6f;

        // Settings (rightmost)
        MakeIconButton("Settings", barRT, x, settingsIcon, "S", OnSettingsClick);
        x -= IconSize + IconGap;

        // Merge
        mergeImage = MakeIconButton("Merge", barRT, x, mergeIcon, "M", OnMergeClick);
        x -= IconSize + IconGap;

        // Grimoire (leftmost)
        MakeIconButton("Grimoire", barRT, x, grimoireIcon, "G", OnGrimoireClick);
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

    // ── Button handlers ──────────────────────────────────────────────────────

    private void OnGrimoireClick() => ToggleGrimoire();
    private void OnMergeClick()    => ToggleMerge();
    private void OnSettingsClick() { /* TODO: settings menu */ }

    private void ToggleGrimoire()
    {
        if (grimoireUI != null) grimoireUI.Toggle();
    }

    private void ToggleMerge()
    {
        if (!IsMergeStage()) return;
        if (mergeRitualUI != null) mergeRitualUI.Toggle();
    }

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
