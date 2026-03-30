using System.Collections;
using UnityEngine;

/// <summary>
/// Plays looping background music with optional fade-in.
/// Add to a GameObject in the dungeon scene — it will persist across stage transitions.
/// Wire the AudioClip in the Inspector.
/// </summary>
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Music")]
    [SerializeField] private AudioClip musicClip;
    [SerializeField] [Range(0f, 1f)] private float targetVolume = 0.5f;

    [Header("Fade")]
    [SerializeField] private float fadeInDuration  = 2f;
    [SerializeField] private float fadeOutDuration = 1f;

    private AudioSource audioSource;
    private Coroutine activeFade;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // New instance takes over — the gameplay scene's fully-wired MusicManager
            // should replace any minimally-configured cutscene/bootstrap instance.
            Destroy(Instance.gameObject);
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip   = musicClip;
        audioSource.loop   = true;
        audioSource.volume = 0f;
        audioSource.playOnAwake = false;
    }

    private void Start()
    {
        // Apply persisted volume setting before the fade begins.
        targetVolume = SettingsData.MusicVolume;

        if (musicClip != null)
            StartFade(FadeIn());
        else
            Debug.LogWarning("[MusicManager] No music clip assigned.");
    }

    public void FadeOut()
    {
        StartFade(FadeOutCoroutine());
    }

    /// <summary>Fades out and pauses — preserves playback position for Resume().</summary>
    public void Pause()
    {
        StartFade(FadePauseCoroutine());
    }

    /// <summary>Unpauses and fades back in to the previous target volume.</summary>
    public void Resume()
    {
        StartFade(FadeResumeCoroutine());
    }

    private void StartFade(IEnumerator fade)
    {
        if (activeFade != null) StopCoroutine(activeFade);
        activeFade = StartCoroutine(fade);
    }

    private IEnumerator FadeIn()
    {
        audioSource.Play();
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            audioSource.volume = Mathf.Lerp(0f, targetVolume, elapsed / fadeInDuration);
            yield return null;
        }
        audioSource.volume = targetVolume;
        activeFade = null;
    }

    private IEnumerator FadeOutCoroutine()
    {
        float startVolume = audioSource.volume;
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeOutDuration);
            yield return null;
        }
        audioSource.volume = 0f;
        audioSource.Stop();
        activeFade = null;
    }

    private IEnumerator FadePauseCoroutine()
    {
        float startVolume = audioSource.volume;
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeOutDuration);
            yield return null;
        }
        audioSource.volume = 0f;
        audioSource.Pause();
        activeFade = null;
    }

    private IEnumerator FadeResumeCoroutine()
    {
        audioSource.UnPause();
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            audioSource.volume = Mathf.Lerp(0f, targetVolume, elapsed / fadeInDuration);
            yield return null;
        }
        audioSource.volume = targetVolume;
        activeFade = null;
    }

    public void SetVolume(float volume)
    {
        targetVolume = Mathf.Clamp01(volume);
        // Only snap to new volume if no fade is active; otherwise the fade coroutine
        // already owns audioSource.volume and will land at the new targetVolume.
        if (activeFade == null)
            audioSource.volume = targetVolume;
    }
}
