using UnityEngine;

/// <summary>
/// Singleton for one-shot SFX playback. Wire clips in the Inspector.
/// Add to a persistent GameObject in the dungeon scene.
/// </summary>
public class SFXManager : MonoBehaviour
{
    public static SFXManager Instance { get; private set; }

    [Header("Merge Spell SFX")]
    [SerializeField] private AudioClip mergeSpellClip;
    [SerializeField] [Range(0f, 1f)] private float mergeSpellVolume = 1f;

    [Header("Player Hit SFX")]
    [SerializeField] private AudioClip playerHitClip;
    [SerializeField] [Range(0f, 1f)] private float playerHitVolume = 1f;

    [Header("Scroll Open SFX")]
    [SerializeField] private AudioClip scrollOpenClip;
    [SerializeField] [Range(0f, 1f)] private float scrollOpenVolume = 1f;

    private AudioSource audioSource;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    public void PlayMergeSpell() => audioSource.PlayOneShot(mergeSpellClip, mergeSpellVolume);
    public void PlayPlayerHit()  => audioSource.PlayOneShot(playerHitClip,  playerHitVolume);
    public void PlayScrollOpen() => audioSource.PlayOneShot(scrollOpenClip, scrollOpenVolume);
}
