using System.Collections;
using UnityEngine;

/// <summary>
/// 12-voice pooled SFX manager with proximity-based volume falloff and distance-based
/// voice stealing. Non-positional (UI/player) sounds bypass distance calculation.
/// Wire all clips and per-clip volumes in the Inspector.
/// </summary>
public class SFXManager : MonoBehaviour
{
    public static SFXManager Instance { get; private set; }

    private const int   PoolSize         = 12;
    private const float MinAudibleVolume = 0.01f;

    // ── Distance falloff ──────────────────────────────────────────────────────
    [Header("Distance Falloff")]
    [SerializeField] private float maxAudibleDistance = 20f;
    // Full volume near the listener, soft fade toward maxAudibleDistance.
    // X axis = distance / maxAudibleDistance (0–1). Y axis = volume multiplier (0–1).
    [SerializeField] private AnimationCurve distanceFalloff = new AnimationCurve(
        new Keyframe(0.00f, 1.0f, 0f,   0f),
        new Keyframe(0.35f, 1.0f, 0f,  -2f),
        new Keyframe(1.00f, 0.0f, -2f,  0f));

    // ── UI / Player SFX ───────────────────────────────────────────────────────
    [Header("Button Click SFX")]
    [SerializeField] private AudioClip buttonClickClip;
    [SerializeField, Range(0f, 1f)] private float buttonClickVolume = 0.7f;

    [Header("Player Shoot SFX")]
    [SerializeField] private AudioClip playerShootClip;
    [SerializeField, Range(0f, 1f)]   private float playerShootVolume     = 0.8f;
    [SerializeField, Range(0f, 0.5f)] private float playerShootFadeOut    = 0.15f;

    [Header("Merge Spell SFX")]
    [SerializeField] private AudioClip mergeSpellClip;
    [SerializeField, Range(0f, 1f)] private float mergeSpellVolume = 1f;

    [Header("Equip Spell SFX")]
    [SerializeField] private AudioClip equipSpellClip;
    [SerializeField, Range(0f, 1f)] private float equipSpellVolume = 0.9f;

    [Header("Skill Cast SFX")]
    [SerializeField] private AudioClip skillCastClip;
    [SerializeField, Range(0f, 1f)] private float skillCastVolume = 0.9f;

    [Header("Ultimate Cast SFX")]
    [SerializeField] private AudioClip ultimateCastClip;
    [SerializeField, Range(0f, 1f)] private float ultimateCastVolume = 1f;

    [Header("Player Hit SFX")]
    [SerializeField] private AudioClip playerHitClip;
    [SerializeField, Range(0f, 1f)] private float playerHitVolume = 1f;

    [Header("Player Death SFX")]
    [SerializeField] private AudioClip playerDeathClip;
    [SerializeField, Range(0f, 1f)] private float playerDeathVolume = 1f;

    [Header("Crit Hit SFX")]
    [SerializeField] private AudioClip critHitClip;
    [SerializeField, Range(0f, 1f)] private float critHitVolume = 1f;

    [Header("Enemy Hit SFX")]
    [SerializeField] private AudioClip enemyHitClip;
    [SerializeField, Range(0f, 1f)]   private float enemyHitVolume = 0.5f;
    [SerializeField, Range(0.1f, 2f)] private float enemyHitPitch  = 0.35f;

    [Header("Enemy Death SFX")]
    [SerializeField] private AudioClip enemyDeathClip;
    [SerializeField, Range(0f, 1f)]   private float enemyDeathVolume = 0.9f;

    [Header("Wall Hit SFX")]
    [SerializeField] private AudioClip wallHitClip;
    [SerializeField, Range(0f, 1f)]   private float wallHitVolume = 0.6f;
    [SerializeField, Range(0.1f, 2f)] private float wallHitPitch  = 1f;

    [Header("Scroll Open SFX")]
    [SerializeField] private AudioClip scrollOpenClip;
    [SerializeField, Range(0f, 1f)] private float scrollOpenVolume = 1f;

    [Header("Heal Scroll SFX")]
    [SerializeField] private AudioClip healScrollClip;
    [SerializeField, Range(0f, 1f)] private float healScrollVolume = 0.9f;

    // ── Typewriter SFX ────────────────────────────────────────────────────────
    [Header("Typewriter SFX")]
    [SerializeField] private AudioClip wordsAppearingClip;
    [SerializeField, Range(0f, 1f)] private float wordsAppearingVolume = 0.55f;

    // ── Cutscene SFX ──────────────────────────────────────────────────────────
    [Header("Cutscene SFX")]
    [SerializeField] private AudioClip heartbeatClip;
    [SerializeField, Range(0f, 1f)] private float heartbeatVolume = 1f;
    [SerializeField] private AudioClip cutsceneEndingClip;
    [SerializeField, Range(0f, 1f)] private float cutsceneEndingVolume = 1f;

    // ── Ghost SFX ─────────────────────────────────────────────────────────────
    [Header("Ghost SFX")]
    [SerializeField] private AudioClip ghostThrowClip;
    [SerializeField, Range(0f, 1f)] private float ghostThrowVolume = 0.8f;
    [SerializeField] private AudioClip ghostSplatClip;
    [SerializeField, Range(0f, 1f)] private float ghostSplatVolume = 0.9f;

    // ── Vampire SFX ───────────────────────────────────────────────────────────
    [Header("Vampire SFX")]
    [SerializeField] private AudioClip vampireChargeUpClip;
    [SerializeField, Range(0f, 1f)] private float vampireChargeUpVolume = 0.85f;
    [SerializeField] private AudioClip vampireShootClip;
    [SerializeField, Range(0f, 1f)] private float vampireShootVolume = 1f;

    // ── Pool internals ────────────────────────────────────────────────────────
    private AudioSource[] _pool;
    private Vector2[]     _poolWorldPos;  // world position each voice was fired from

    private float _sfxVolumeMultiplier  = 1f;
    private float _lastTypewriterTick   = -999f;

    // ── Gameplay heartbeat (low-health) ───────────────────────────────────────
    private AudioSource  _heartbeatSource;
    private Coroutine    _heartbeatFadeCoroutine;
    private bool         _heartbeatFading;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // New instance takes over — the gameplay scene's fully-wired SFXManager
            // should replace any minimally-configured cutscene/bootstrap instance.
            Destroy(Instance.gameObject);
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _pool         = new AudioSource[PoolSize];
        _poolWorldPos = new Vector2[PoolSize];

        for (int i = 0; i < PoolSize; i++)
        {
            _pool[i]              = gameObject.AddComponent<AudioSource>();
            _pool[i].playOnAwake  = false;
            _pool[i].spatialBlend = 0f; // always 2D — volume is managed manually
        }

        // Dedicated looping source for gameplay heartbeat — lives outside the pool
        _heartbeatSource             = gameObject.AddComponent<AudioSource>();
        _heartbeatSource.playOnAwake = false;
        _heartbeatSource.spatialBlend = 0f;
        _heartbeatSource.loop        = true;
    }

    private void Start()
    {
        _sfxVolumeMultiplier = SettingsData.SfxVolume;
    }

    public void SetSfxVolume(float volume)
    {
        _sfxVolumeMultiplier = Mathf.Clamp01(volume);
    }

    // ── Gameplay heartbeat API ─────────────────────────────────────────────────

    /// <summary>
    /// Call every frame while the player is in the critical health zone.
    /// Starts the looping heartbeat if not already playing and adjusts pitch to match
    /// <paramref name="bpm"/>. Volume ducks very slightly at higher BPM.
    /// </summary>
    public void StartOrUpdateHeartbeat(float bpm)
    {
        if (heartbeatClip == null) return;

        // Pitch the looping clip so one loop = one beat interval
        float pitch = Mathf.Clamp(heartbeatClip.length * bpm / 60f, 0.1f, 3f);

        // Subtle duck: 1.0x at 45 BPM, 0.88x at 65 BPM
        float duckT       = Mathf.Clamp01((bpm - 45f) / 20f);
        float targetVolume = heartbeatVolume * _sfxVolumeMultiplier * Mathf.Lerp(1f, 0.88f, duckT);

        _heartbeatSource.pitch = pitch;

        if (!_heartbeatSource.isPlaying || _heartbeatFading)
        {
            // Cancel any fade-out in progress and fade in
            _heartbeatFading = false;
            if (_heartbeatFadeCoroutine != null) StopCoroutine(_heartbeatFadeCoroutine);

            if (!_heartbeatSource.isPlaying)
            {
                _heartbeatSource.clip   = heartbeatClip;
                _heartbeatSource.volume = 0f;
                _heartbeatSource.Play();
            }

            _heartbeatFadeCoroutine = StartCoroutine(FadeHeartbeatVolume(targetVolume, 0.5f));
        }
        else
        {
            _heartbeatSource.volume = targetVolume;
        }
    }

    /// <summary>Fades out and stops the gameplay heartbeat loop over 1 second.</summary>
    public void StopHeartbeat()
    {
        if (!_heartbeatSource.isPlaying || _heartbeatFading) return;
        if (_heartbeatFadeCoroutine != null) StopCoroutine(_heartbeatFadeCoroutine);
        _heartbeatFading        = true;
        _heartbeatFadeCoroutine = StartCoroutine(FadeHeartbeatVolume(0f, 1f, stopOnComplete: true));
    }

    /// <summary>Stops the gameplay heartbeat immediately with no fade (use on player death).</summary>
    public void StopHeartbeatImmediate()
    {
        if (_heartbeatFadeCoroutine != null) StopCoroutine(_heartbeatFadeCoroutine);
        _heartbeatFading = false;
        _heartbeatSource.Stop();
    }

    private IEnumerator FadeHeartbeatVolume(float target, float duration, bool stopOnComplete = false)
    {
        float start = _heartbeatSource.volume;
        float t     = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            _heartbeatSource.volume = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }
        _heartbeatSource.volume = target;
        if (stopOnComplete)
        {
            _heartbeatSource.Stop();
            _heartbeatFading = false;
        }
    }

    // ── Public play API ───────────────────────────────────────────────────────

    /// <summary>Play a positional sound. Volume falls off with distance from the camera.</summary>
    public void PlayAtPosition(AudioClip clip, float baseVolume, Vector2 worldPos)
    {
        if (clip == null) return;
        float vol = ComputeVolume(baseVolume, worldPos);
        if (vol < MinAudibleVolume) return;
        PlayOnVoice(clip, vol, 1f, worldPos);
    }

    /// <summary>Play a positional sound with a custom pitch (used for Vampire charge-up duration matching).</summary>
    public void PlayAtPositionWithPitch(AudioClip clip, float baseVolume, Vector2 worldPos, float pitch)
    {
        if (clip == null) return;
        float vol = ComputeVolume(baseVolume, worldPos);
        if (vol < MinAudibleVolume) return;
        PlayOnVoice(clip, vol, pitch, worldPos);
    }

    /// <summary>Play a non-positional (UI / player) sound at full distance-independent volume.</summary>
    public void PlayUI(AudioClip clip, float baseVolume)
    {
        if (clip == null) return;
        float vol = baseVolume * _sfxVolumeMultiplier;
        if (vol < MinAudibleVolume) return;
        // UI sounds are treated as originating at the listener — lowest steal priority.
        PlayOnVoice(clip, vol, 1f, GetListenerPos());
    }

    // ── Named helpers ─────────────────────────────────────────────────────────

    public void PlayButtonClick()      => PlayUI(buttonClickClip,     buttonClickVolume);

    public void PlayPlayerShoot()
    {
        if (playerShootClip == null) return;
        float vol = playerShootVolume * _sfxVolumeMultiplier;
        if (vol < MinAudibleVolume) return;

        AudioSource voice = AcquireVoice(GetListenerPos());
        if (voice == null) return;

        voice.clip   = playerShootClip;
        voice.volume = vol;
        voice.pitch  = 1f;
        voice.Play();

        int idx = System.Array.IndexOf(_pool, voice);
        if (idx >= 0) _poolWorldPos[idx] = GetListenerPos();

        float holdTime = Mathf.Max(0f, playerShootClip.length - playerShootFadeOut);
        StartCoroutine(FadeOutVoice(voice, vol, holdTime, playerShootFadeOut));
    }
    public void PlayWallHit(Vector2 pos)
    {
        if (wallHitClip == null) return;
        float vol = ComputeVolume(wallHitVolume, pos);
        if (vol < MinAudibleVolume) return;
        PlayOnVoice(wallHitClip, vol, wallHitPitch, pos);
    }

    public void PlayEnemyHit(Vector2 pos)
    {
        if (enemyHitClip == null) return;
        float vol = ComputeVolume(enemyHitVolume, pos);
        if (vol < MinAudibleVolume) return;
        PlayOnVoice(enemyHitClip, vol, enemyHitPitch, pos);
    }
    public void PlayCritHit(Vector2 pos)    => PlayUI(critHitClip, critHitVolume);
    public void PlayEnemyDeath(Vector2 pos) => PlayAtPosition(enemyDeathClip, enemyDeathVolume, pos);
    public void PlayMergeSpell()      => PlayUI(mergeSpellClip,      mergeSpellVolume);
    public void PlayEquipSpell()      => PlayUI(equipSpellClip,      equipSpellVolume);
    public void PlaySkillCast()       => PlayUI(skillCastClip,       skillCastVolume);
    public void PlayUltimateCast()    => PlayUI(ultimateCastClip,    ultimateCastVolume);
    public void PlayPlayerHit()       => PlayUI(playerHitClip,       playerHitVolume);
    public void PlayPlayerDeath()     => PlayUI(playerDeathClip,     playerDeathVolume);
    public void PlayScrollOpen()      => PlayUI(scrollOpenClip,       scrollOpenVolume);
    public void PlayHealScroll()      => PlayUI(healScrollClip,        healScrollVolume);
    /// <summary>Plays the heartbeat clip pitched so it lasts exactly <paramref name="targetDuration"/> seconds.</summary>
    public void PlayHeartbeat(float targetDuration)
    {
        if (heartbeatClip == null) return;
        float pitch = Mathf.Clamp(heartbeatClip.length / Mathf.Max(targetDuration, 0.05f), 0.15f, 3.0f);
        float vol   = heartbeatVolume * _sfxVolumeMultiplier;
        if (vol < MinAudibleVolume) return;
        PlayOnVoice(heartbeatClip, vol, pitch, GetListenerPos());
    }
    public void PlayCutsceneEnding()  => PlayUI(cutsceneEndingClip,   cutsceneEndingVolume);

    /// <summary>
    /// Plays one typewriter tick with randomised pitch and volume for natural chaining depth.
    /// Skip spaces — they produce no visible character and would cause a sound burst on word breaks.
    /// </summary>
    public void PlayTypewriterTick(char ch)
    {
        if (wordsAppearingClip == null) return;
        if (ch == ' ' || ch == '\t') return;          // no audible character appeared

        // Throttle: fire at most once every 0.12 s so rapid text doesn't blur into noise
        float now = Time.unscaledTime;
        if (now - _lastTypewriterTick < 0.12f) return;
        _lastTypewriterTick = now;

        float vol   = wordsAppearingVolume * _sfxVolumeMultiplier * Random.Range(0.70f, 1.00f);
        float pitch = Random.Range(0.88f, 1.14f);
        if (vol < MinAudibleVolume) return;
        PlayOnVoice(wordsAppearingClip, vol, pitch, GetListenerPos());
    }

    public void PlayGhostThrow(Vector2 pos) => PlayAtPosition(ghostThrowClip, ghostThrowVolume, pos);
    public void PlayGhostSplat(Vector2 pos) => PlayAtPosition(ghostSplatClip, ghostSplatVolume, pos);

    public void PlayVampireShoot(Vector2 pos) => PlayAtPosition(vampireShootClip, vampireShootVolume, pos);

    /// <summary>
    /// Plays VampireChargeUp pitched so it finishes exactly when chargeDuration elapses.
    /// </summary>
    public void PlayVampireChargeUp(Vector2 pos, float chargeDuration)
    {
        if (vampireChargeUpClip == null) return;
        float pitch = Mathf.Clamp(vampireChargeUpClip.length / Mathf.Max(chargeDuration, 0.05f), 0.1f, 3f);
        PlayAtPositionWithPitch(vampireChargeUpClip, vampireChargeUpVolume, pos, pitch);
    }

    // ── Fade helpers ──────────────────────────────────────────────────────────

    private IEnumerator FadeOutVoice(AudioSource source, float peakVolume, float holdSeconds, float fadeDuration)
    {
        // Hold phase — wait until the fade window begins
        float elapsed = 0f;
        while (elapsed < holdSeconds)
        {
            if (!source.isPlaying) yield break;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Fade phase — ramp volume to zero then stop
        float t = 0f;
        while (t < fadeDuration)
        {
            if (!source.isPlaying) yield break;
            source.volume = Mathf.Lerp(peakVolume, 0f, t / fadeDuration);
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        source.Stop();
        source.volume = peakVolume; // restore so the next sound isn't silenced
    }

    // ── Voice pool ────────────────────────────────────────────────────────────

    private void PlayOnVoice(AudioClip clip, float volume, float pitch, Vector2 worldPos)
    {
        AudioSource voice = AcquireVoice(worldPos);
        if (voice == null) return;

        voice.clip   = clip;
        voice.volume = volume;
        voice.pitch  = pitch;
        voice.Play();

        int idx = System.Array.IndexOf(_pool, voice);
        if (idx >= 0) _poolWorldPos[idx] = worldPos;
    }

    private AudioSource AcquireVoice(Vector2 incomingPos)
    {
        // Prefer a free (non-playing) voice
        for (int i = 0; i < PoolSize; i++)
            if (!_pool[i].isPlaying) return _pool[i];

        // All busy — steal the voice whose origin is furthest from the listener
        Vector2 listenerPos = GetListenerPos();
        int     stealIdx    = 0;
        float   maxDist     = -1f;

        for (int i = 0; i < PoolSize; i++)
        {
            float d = Vector2.Distance(_poolWorldPos[i], listenerPos);
            if (d > maxDist) { maxDist = d; stealIdx = i; }
        }

        _pool[stealIdx].Stop();
        return _pool[stealIdx];
    }

    private float ComputeVolume(float baseVolume, Vector2 worldPos)
    {
        float dist       = Vector2.Distance(worldPos, GetListenerPos());
        float normalized = Mathf.Clamp01(dist / maxAudibleDistance);
        float falloff    = distanceFalloff.Evaluate(normalized);
        return baseVolume * falloff * _sfxVolumeMultiplier;
    }

    private static Vector2 GetListenerPos()
    {
        return Camera.main != null ? (Vector2)Camera.main.transform.position : Vector2.zero;
    }
}
