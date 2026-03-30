using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Self-contained UI click sound — owns its own AudioSource so it works in any
/// scene regardless of whether SFXManager exists yet.
///
/// Add to any persistent or scene-root GameObject and wire the clip in the Inspector.
/// Covers all canvases and dynamically created UI automatically.
/// </summary>
public class UIClickSFX : MonoBehaviour
{
    [SerializeField] private AudioClip clip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.7f;

    private AudioSource _source;

    private void Awake()
    {
        _source              = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake  = false;
        _source.spatialBlend = 0f;
    }

    private void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (EventSystem.current == null) return;
        if (!EventSystem.current.IsPointerOverGameObject()) return;
        if (clip == null) return;

        // Use the global SFX volume multiplier if SFXManager is available,
        // otherwise fall back to the local volume field directly.
        float vol = SFXManager.Instance != null
            ? volume * SettingsData.SfxVolume
            : volume;

        _source.PlayOneShot(clip, vol);
    }
}
