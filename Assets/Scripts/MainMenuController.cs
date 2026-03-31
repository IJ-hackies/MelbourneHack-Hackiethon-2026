using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
