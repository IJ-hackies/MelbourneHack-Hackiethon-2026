using System;
using System.Collections;
using Cinemachine;
using UnityEngine;

/// <summary>
/// Mario-style death cutscene:
///   1. Screen freezes (timeScale = 0)
///   2. Camera zooms in on the player (Cinemachine Brain disabled, direct control)
///   3. Game-over audio plays
///   4. Player bounces up then falls off screen
///   5. Invokes onComplete → GameOverUI shows
///
/// Setup:
///   1. Add to any scene GameObject (StageDirector's GO is fine).
///   2. Assign gameOverClip in Inspector.
///   3. StageDirector calls Play(playerGO, callback).
/// </summary>
public class PlayerDeathCutscene : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip gameOverClip;
    [SerializeField] [Range(0f, 1f)] private float audioVolume = 1f;

    [Header("Timing (all real-time seconds)")]
    [SerializeField] private float freezePause     = 0.35f;   // freeze before zoom starts
    [SerializeField] private float zoomDuration    = 0.55f;   // camera zoom-in
    [SerializeField] private float pauseAfterZoom  = 0.15f;   // pause between zoom and bounce
    [SerializeField] private float bounceUpTime    = 0.32f;   // player moves up
    [SerializeField] private float preShowDelay    = 0.25f;   // pause after fall before UI

    [Header("Camera Zoom")]
    [Tooltip("Target orthographic size as a fraction of the current size.")]
    [SerializeField] [Range(0.25f, 0.85f)] private float zoomFactor  = 0.45f;
    [SerializeField]                       private float zoomMinSize  = 1.8f;

    [Header("Player Bounce")]
    [SerializeField] private float bounceUpDistance = 3.5f;   // world units upward
    [SerializeField] private float fallGravity       = 30f;    // world units/s² downward

    // ── Public API ────────────────────────────────────────────────────────────

    public void Play(GameObject player, Action onComplete)
    {
        StartCoroutine(RunSequence(player, onComplete));
    }

    // ── Sequence ──────────────────────────────────────────────────────────────

    private IEnumerator RunSequence(GameObject player, Action onComplete)
    {
        if (player == null) { onComplete?.Invoke(); yield break; }

        // ── 1. Freeze ─────────────────────────────────────────────────────────
        Time.timeScale = 0f;

        // Disable player input / physics
        var pm = player.GetComponent<PlayerMovement>();
        var rb = player.GetComponent<Rigidbody2D>();
        var pa = player.GetComponent<PlayerAnimator>();
        if (pm) pm.enabled = false;
        if (rb) { rb.linearVelocity = Vector2.zero; rb.bodyType = RigidbodyType2D.Kinematic; }
        if (pa)
        {
            pa.enabled = false;
            var anim = player.GetComponent<Animator>();
            if (anim) anim.Play("idle_south");
        }

        // Grab camera refs and disable Cinemachine so we drive the camera directly
        var cam = Camera.main;
        CinemachineBrain brain = cam != null ? cam.GetComponent<CinemachineBrain>() : null;
        if (brain != null) brain.enabled = false;

        // Snap camera onto player (keep Z)
        if (cam != null)
        {
            Vector3 cp = cam.transform.position;
            cp.x = player.transform.position.x;
            cp.y = player.transform.position.y;
            cam.transform.position = cp;
        }

        // ── 2. Play audio (unaffected by timeScale) ───────────────────────────
        if (gameOverClip != null)
        {
            Vector3 audioPos = cam != null ? cam.transform.position : player.transform.position;
            AudioSource.PlayClipAtPoint(gameOverClip, audioPos, audioVolume);
        }

        // ── 3. Brief freeze pause ─────────────────────────────────────────────
        yield return new WaitForSecondsRealtime(freezePause);

        // ── 4. Zoom in ────────────────────────────────────────────────────────
        if (cam != null)
        {
            float startSize  = cam.orthographicSize;
            float targetSize = Mathf.Max(startSize * zoomFactor, zoomMinSize);
            float elapsed    = 0f;

            while (elapsed < zoomDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t       = Mathf.Clamp01(elapsed / zoomDuration);
                float smooth  = t * t * (3f - 2f * t); // smoothstep
                cam.orthographicSize = Mathf.Lerp(startSize, targetSize, smooth);
                yield return null;
            }
            cam.orthographicSize = Mathf.Max(startSize * zoomFactor, zoomMinSize);
        }

        yield return new WaitForSecondsRealtime(pauseAfterZoom);

        // ── 5. Player bounces up ──────────────────────────────────────────────
        Vector3 startPos = player.transform.position;
        Vector3 peakPos  = startPos + Vector3.up * bounceUpDistance;

        float upElapsed = 0f;
        while (upElapsed < bounceUpTime)
        {
            upElapsed += Time.unscaledDeltaTime;
            float t     = Mathf.Clamp01(upElapsed / bounceUpTime);
            float eased = 1f - (1f - t) * (1f - t); // ease-out quad
            player.transform.position = Vector3.Lerp(startPos, peakPos, eased);
            yield return null;
        }
        player.transform.position = peakPos;

        // ── 6. Player falls off screen ────────────────────────────────────────
        float offScreenY = cam != null
            ? cam.transform.position.y - cam.orthographicSize - 6f
            : player.transform.position.y - 30f;

        float    fallVel = 0f;
        Vector3  pos     = player.transform.position;
        while (pos.y > offScreenY)
        {
            fallVel += fallGravity * Time.unscaledDeltaTime;
            pos.y   -= fallVel     * Time.unscaledDeltaTime;
            player.transform.position = pos;
            yield return null;
        }

        // ── 7. Brief pause then hand off to Game Over UI ──────────────────────
        yield return new WaitForSecondsRealtime(preShowDelay);

        onComplete?.Invoke();
    }
}
