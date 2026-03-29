using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [Header("Background")]
    [SerializeField] private Image backgroundImage;

    [Header("Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button quitButton;

    private void Start()
    {
        // Auto-find buttons by name if not wired in Inspector
        if (playButton == null)
        {
            var go = GameObject.Find("PlayButton");
            if (go != null) playButton = go.GetComponent<Button>();
        }
        if (quitButton == null)
        {
            var go = GameObject.Find("QuitButton");
            if (go != null) quitButton = go.GetComponent<Button>();
        }

        if (playButton != null) playButton.onClick.AddListener(OnPlay);
        else Debug.LogWarning("[MainMenuController] PlayButton not found.");
        if (quitButton != null) quitButton.onClick.AddListener(OnQuit);
    }

    private void OnPlay()
    {
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
