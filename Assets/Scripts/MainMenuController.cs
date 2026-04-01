using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("Background")]
    [SerializeField] private Image backgroundImage;

    [Header("Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    [Header("Settings")]
    [SerializeField] private SettingsUI settingsUI;


    private void Start()
    {
        // Auto-find buttons by name if not wired in Inspector
        if (playButton == null)
        {
            var go = GameObject.Find("PlayButton");
            if (go != null) playButton = go.GetComponent<Button>();
        }
        if (settingsButton == null)
        {
            var go = GameObject.Find("SettingsButton");
            if (go != null) settingsButton = go.GetComponent<Button>();
        }
        if (quitButton == null)
        {
            var go = GameObject.Find("QuitButton");
            if (go != null) quitButton = go.GetComponent<Button>();
        }
        if (settingsUI == null)
            settingsUI = FindAnyObjectByType<SettingsUI>();

        if (playButton != null)     playButton.onClick.AddListener(OnPlay);
        else Debug.LogWarning("[MainMenuController] PlayButton not found.");
        if (settingsButton != null) settingsButton.onClick.AddListener(OnSettings);
        if (quitButton != null)     quitButton.onClick.AddListener(OnQuit);

        BuildFurthestPageDisplay();
    }

    private void BuildFurthestPageDisplay()
    {
        var canvasGO          = new GameObject("FurthestPage_Canvas");
        var canvas            = canvasGO.AddComponent<Canvas>();
        canvas.renderMode     = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder   = 50;
        var scaler            = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode    = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // Background pill — top-right, matching HUD counter style
        var bgGO       = new GameObject("FurthestPageBg");
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgRT       = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.one;
        bgRT.anchorMax = Vector2.one;
        bgRT.pivot     = Vector2.one;
        bgRT.anchoredPosition = new Vector2(-20f, -40f);
        bgRT.sizeDelta = new Vector2(400f, 50f);
        var bgImg      = bgGO.AddComponent<Image>();
        bgImg.color    = new Color(0f, 0f, 0f, 0f);

        // Text
        var textGO       = new GameObject("FurthestPageText");
        textGO.transform.SetParent(bgGO.transform, false);
        var textRT       = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        var tmp          = textGO.AddComponent<TextMeshProUGUI>();
        if (SettingsUI.Instance != null) tmp.font = SettingsUI.Instance.Font;
        tmp.text         = $"Furthest Page: {PageTracker.FurthestPage}";
        tmp.fontSize     = 40f;
        tmp.fontStyle    = FontStyles.Bold;
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.color        = new Color(1f, 0.45f, 0.05f); // fiery orange
        tmp.raycastTarget = false;
    }

    private void OnSettings() => (SettingsUI.Instance ?? settingsUI)?.Open(false);

    private void OnPlay()
    {
        // Play cutscene first if the scene exists, otherwise go straight to gameplay
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

    private void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Call this once the background art is ready:
    // drag the Sprite onto backgroundImage in the Inspector, or call this from code
    public void SetBackground(Sprite sprite)
    {
        if (backgroundImage != null)
            backgroundImage.sprite = sprite;
    }
}
