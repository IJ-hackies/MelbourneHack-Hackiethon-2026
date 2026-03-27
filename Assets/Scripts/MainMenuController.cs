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
        if (playButton != null) playButton.onClick.AddListener(OnPlay);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuit);
    }

    private void OnPlay()
    {
        // TODO: replace "GameScene" with your actual gameplay scene name
        SceneManager.LoadScene("GameScene");
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
