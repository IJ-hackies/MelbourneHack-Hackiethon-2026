using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("Background")]
    [SerializeField] private Image backgroundImage;

    [Header("Font")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Settings")]
    [SerializeField] private SettingsUI settingsUI;

    // ── Parchment colours ────────────────────────────────────────────────────
    private static readonly Color ColPanel     = new Color(0.16f, 0.12f, 0.08f, 0.97f);
    private static readonly Color ColBorder    = new Color(0.85f, 0.65f, 0.15f, 0.6f);
    private static readonly Color ColInner     = new Color(0.14f, 0.10f, 0.06f, 0.97f);
    private static readonly Color ColBtnPlay   = new Color(0.55f, 0.12f, 0.08f);
    private static readonly Color ColBtnSmall  = new Color(0.22f, 0.16f, 0.09f);
    private static readonly Color ColText      = new Color(0.93f, 0.87f, 0.72f);
    private static readonly Color ColGold      = new Color(0.85f, 0.65f, 0.15f);

    private GameObject _canvasGO;

    private void Start()
    {
        if (settingsUI == null)
            settingsUI = FindAnyObjectByType<SettingsUI>();

        // Ensure an EventSystem exists
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();
        }

        BuildMenuUI();
    }

    private void OnDestroy()
    {
        if (_canvasGO != null) Destroy(_canvasGO);
    }

    // ── Build ────────────────────────────────────────────────────────────────

    private void BuildMenuUI()
    {
        _canvasGO = new GameObject("MainMenu_Canvas");
        var canvas = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGO.AddComponent<GraphicRaycaster>();

        // ── PLAY — large parchment button, center-lower ────────────────────
        BuildParchmentButton("PlayBtn", _canvasGO.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -160f), new Vector2(360f, 90f),
            "PLAY", 34f, ColBtnPlay, OnPlay);

        // ── SETTINGS — bottom-left ──────────────────────────────────────────
        BuildParchmentButton("SettingsBtn", _canvasGO.transform,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(40f, 40f), new Vector2(320f, 80f),
            "SETTINGS", 28f, ColBtnSmall, OnSettings);

        // ── TUTORIAL — bottom-right ─────────────────────────────────────────
        BuildParchmentButton("TutorialBtn", _canvasGO.transform,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-40f, 40f), new Vector2(320f, 80f),
            "TUTORIAL", 28f, ColBtnSmall, OnTutorial);

        // ── Furthest Page — top-right ───────────────────────────────────────
        BuildFurthestPageDisplay();
    }

    private void BuildParchmentButton(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size,
        string label, float fontSize, Color bgColor,
        UnityEngine.Events.UnityAction onClick)
    {
        // Outer panel
        var outerRT = MakeRT(name, parent, anchorMin, anchorMax, pivot, pos, size);
        var outerImg = outerRT.gameObject.AddComponent<Image>();
        outerImg.color = ColPanel;

        // Gold border
        var borderRT = MakeRT("Border", outerRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        borderRT.offsetMin = new Vector2(3f, 3f);
        borderRT.offsetMax = new Vector2(-3f, -3f);
        var borderImg = borderRT.gameObject.AddComponent<Image>();
        borderImg.color = ColBorder;
        borderImg.raycastTarget = false;

        // Inner fill — this is the clickable surface
        var innerRT = MakeRT("Inner", borderRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        innerRT.offsetMin = new Vector2(2f, 2f);
        innerRT.offsetMax = new Vector2(-2f, -2f);
        var innerImg = innerRT.gameObject.AddComponent<Image>();
        innerImg.color = bgColor;

        // Button component on the outer panel
        var btn = outerRT.gameObject.AddComponent<Button>();
        btn.targetGraphic = innerImg;
        btn.onClick.AddListener(onClick);
        outerRT.gameObject.AddComponent<UIButtonHover>();

        // Label text
        MakeText("Label", outerRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            label, fontSize, ColText, TextAlignmentOptions.Center);
    }

    private void BuildFurthestPageDisplay()
    {
        var bgRT = MakeRT("FurthestPage", _canvasGO.transform,
            Vector2.one, Vector2.one, Vector2.one,
            new Vector2(-20f, -40f), new Vector2(400f, 50f));

        var tmp = MakeText("Text", bgRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            $"Furthest Page: {PageTracker.FurthestPage}", 40f,
            new Color(1f, 0.45f, 0.05f), TextAlignmentOptions.Center);
    }

    // ── Button handlers ──────────────────────────────────────────────────────

    private void OnSettings() => (SettingsUI.Instance ?? settingsUI)?.Open(false);

    private void OnPlay()
    {
        Grimoire.Instance?.ResetAll();
        UltimateAbility.Instance?.ResetGauge();

        string cutsceneScene = "IntroCutscene";
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (path.Contains(cutsceneScene))
            {
                SceneManager.LoadScene(cutsceneScene);
                return;
            }
        }
        SceneManager.LoadScene("StageTest");
    }

    private void OnTutorial()
    {
        Grimoire.Instance?.ResetAll();
        UltimateAbility.Instance?.ResetGauge();
        SceneManager.LoadScene("Tutorial");
    }

    private void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void SetBackground(Sprite sprite)
    {
        if (backgroundImage != null)
            backgroundImage.sprite = sprite;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private TMP_Text MakeText(string name, Transform parent,
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
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color = color;
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = new Color32(0, 0, 0, 200);
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
