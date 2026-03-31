using System.Collections;
using Cinemachine;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a dramatic intro transition when the Dungeon scene loads, covering both the
/// "watched full cutscene" and "skipped" paths (both arrive with a black screen).
///
/// Sequence:
///   1. Screen starts fully black (matching the cutscene's exit state)
///   2. Fade in — world reveals with the camera zoomed tight on the player
///   3. Freeze for 1 second — the player is centred, nothing moving
///   4. Slow dramatic zoom out to normal gameplay camera size
///   5. Player input re-enabled — gameplay begins
///
/// Setup:
///   Add this component to any GameObject in the Dungeon scene (e.g. StageDirector).
///   No Inspector wiring needed — everything is found automatically.
/// </summary>
public class DungeonIntroTransition : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Duration of the fade-in from black (seconds).")]
    [SerializeField] private float fadeInDuration  = 0.55f;

    [Tooltip("How long the camera freezes on the player before zooming out (seconds).")]
    [SerializeField] private float holdDuration    = 1.0f;

    [Tooltip("Duration of the dramatic zoom-out (seconds).")]
    [SerializeField] private float zoomOutDuration = 2.6f;

    [Header("Camera")]
    [Tooltip("Orthographic size to start the zoom from (close-up). Normal gameplay size is read from the vcam.")]
    [SerializeField] private float zoomInSize = 2.2f;

    // ── Runtime ──────────────────────────────────────────────────────────────
    private Canvas overlayCanvas;
    private Image  fadeOverlay;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Build the black overlay immediately in Awake so there is zero white-flash
        // on the first rendered frame before Start runs.
        BuildOverlay();
    }

    private void Start()
    {
        StartCoroutine(RunTransition());
    }

    // ── Overlay construction ─────────────────────────────────────────────────

    private void BuildOverlay()
    {
        // Prefer the DontDestroyOnLoad canvas created by IntroCutscene — it bridges
        // the scene gap with zero flash. Fall back to creating our own if missing
        // (e.g. when starting the dungeon directly in the Editor without the cutscene).
        var existing = GameObject.Find("SceneTransitionOverlay");
        if (existing != null)
        {
            overlayCanvas = existing.GetComponent<Canvas>();
            fadeOverlay   = existing.GetComponentInChildren<Image>();
            fadeOverlay.color = Color.black;   // ensure fully black
            return;
        }

        // Fallback: create a local overlay so the transition still works in isolation
        var go = new GameObject("DungeonIntroOverlay");
        overlayCanvas                = go.AddComponent<Canvas>();
        overlayCanvas.renderMode     = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder   = 999;
        var scaler                   = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode           = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution   = new Vector2(1920, 1080);
        go.AddComponent<GraphicRaycaster>();

        var imgGO = new GameObject("BlackFill");
        imgGO.transform.SetParent(go.transform, false);
        fadeOverlay                  = imgGO.AddComponent<Image>();
        fadeOverlay.raycastTarget    = false;
        var rt                       = fadeOverlay.rectTransform;
        rt.anchorMin                 = Vector2.zero;
        rt.anchorMax                 = Vector2.one;
        rt.offsetMin                 = Vector2.zero;
        rt.offsetMax                 = Vector2.zero;
        fadeOverlay.color            = Color.black;
    }

    // ── Transition sequence ──────────────────────────────────────────────────

    private IEnumerator RunTransition()
    {
        // ── Find scene objects ───────────────────────────────────────────────
        var playerMove = FindAnyObjectByType<PlayerMovement>();
        var vcam       = FindAnyObjectByType<CinemachineVirtualCamera>();

        // Disable player input for the duration of the transition
        if (playerMove != null) playerMove.enabled = false;

        // Snap camera to the close-up zoom before anything is visible
        float normalSize = vcam != null ? vcam.m_Lens.OrthographicSize : 5f;
        if (vcam != null) vcam.m_Lens.OrthographicSize = zoomInSize;

        // ── 1. Fade in from black ────────────────────────────────────────────
        yield return FadeOverlay(1f, 0f, fadeInDuration, EaseInOutCubic);

        // ── 2. Freeze — hold 1 second on the player ─────────────────────────
        yield return new WaitForSecondsRealtime(holdDuration);

        // ── 3. Dramatic zoom out ─────────────────────────────────────────────
        // EaseInCubic: starts very slow, then accelerates — feels like the world
        // is pulling away with mounting force.
        if (vcam != null)
        {
            float elapsed = 0f;
            while (elapsed < zoomOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseInCubic(Mathf.Clamp01(elapsed / zoomOutDuration));
                vcam.m_Lens.OrthographicSize = Mathf.Lerp(zoomInSize, normalSize, t);
                yield return null;
            }
            vcam.m_Lens.OrthographicSize = normalSize;
        }

        // ── 4. Re-enable player input ────────────────────────────────────────
        if (playerMove != null) playerMove.enabled = true;

        // Cleanup
        Destroy(overlayCanvas.gameObject);
        Destroy(this);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private IEnumerator FadeOverlay(float from, float to, float duration, System.Func<float, float> ease)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = ease(Mathf.Clamp01(elapsed / duration));
            fadeOverlay.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t));
            yield return null;
        }
        fadeOverlay.color = new Color(0f, 0f, 0f, to);
    }

    private static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

    private static float EaseInCubic(float t) => t * t * t;
}
