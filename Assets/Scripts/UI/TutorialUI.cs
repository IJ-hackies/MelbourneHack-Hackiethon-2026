using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tutorial overlay system: parchment dialogue box + spotlight + pointer arrow.
///
/// Spotlight uses 4 dark panels arranged around a target RectTransform to
/// create a visible "window" while dimming the rest. The target's parent
/// canvas sorting order is boosted so it renders above the overlay and
/// receives clicks. A bobbing pointer arrow and dialogue box sit on the
/// highest layer.
///
/// Setup: Attach to a GameObject in the Tutorial scene. Assign font + SFX clips.
/// TutorialDirector drives all calls.
/// </summary>
public class TutorialUI : MonoBehaviour
{
    [Header("Font")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Sound Effects")]
    [SerializeField] private AudioClip pageScrollClip;
    [Tooltip("Volume for page scroll / new dialogue SFX")]
    [SerializeField, Range(0f, 1f)] private float pageScrollVolume = 0.7f;
    [SerializeField] private AudioClip typewriterClip;
    [Tooltip("Volume for typewriter tick SFX")]
    [SerializeField, Range(0f, 1f)] private float typewriterVolume = 0.4f;
    [Tooltip("Seconds per character for typewriter effect")]
    [SerializeField] private float typewriterSpeed = 0.03f;

    // ── Sorting orders ─────────────────────────────────────────────────────
    // Above Grimoire (boosted to 230) and MergeRitual (boosted to 230), below Settings (500)
    private const int OverlayOrder  = 231;
    private const int TargetOrder   = 232;
    private const int PointerOrder  = 233;
    private const int DialogueOrder = 235;

    // ── Overlay canvas (4-panel dim) ────────────────────────────────────────
    private GameObject _overlayCanvasGO;
    private RectTransform _panelTop, _panelBot, _panelLeft, _panelRight;
    private Canvas _overlayCanvas;

    // ── Pointer canvas ──────────────────────────────────────────────────────
    private GameObject _pointerCanvasGO;
    private RectTransform _arrowRT;
    private float _arrowBobPhase;

    // ── Dialogue canvas ─────────────────────────────────────────────────────
    private GameObject _dialogueCanvasGO;
    private RectTransform _dialoguePanel;
    private TMP_Text _dialogueText;
    private TMP_Text _dialogueHint;
    private TMP_Text _speakerText;

    // ── Page flip ───────────────────────────────────────────────────────────
    private RectTransform _pageFlipRT;
    private Image _pageFlipImg;
    private bool _flipping;

    // ── State ────────────────────────────────────────────────────────────────
    private string[] _currentLines;
    private int _lineIndex;
    private Action _onDialogueComplete;
    private bool _dialogueActive;
    private Canvas _boostedCanvas;
    private int _boostedOriginalOrder;
    private float _inputCooldown; // prevents instant-advance on the frame dialogue appears
    private bool _typewriting;   // true while typewriter is revealing characters
    private Coroutine _typewriterCoroutine;
    private AudioSource _typewriterSource; // dedicated source so we can stop it instantly

    // Cached references for overlay checks (to avoid FindAnyObjectByType every frame)
    private GrimoireUI _cachedGrimoire;
    private MergeRitualUI _cachedMerge;
    private MinimapController _cachedMinimap;

    // ── Parchment colours ────────────────────────────────────────────────────
    private static readonly Color ColParchment  = new Color(0.93f, 0.87f, 0.72f, 1f);
    private static readonly Color ColDarkEdge   = new Color(0.55f, 0.42f, 0.22f, 1f);
    private static readonly Color ColBg         = new Color(0.82f, 0.74f, 0.58f, 0.97f);
    private static readonly Color ColText       = new Color(0.12f, 0.08f, 0.04f);
    private static readonly Color ColSpeaker    = new Color(0.45f, 0.12f, 0.08f);
    private static readonly Color ColHint       = new Color(0.45f, 0.38f, 0.28f, 0.7f);
    private static readonly Color ColPageFlip   = new Color(0.88f, 0.80f, 0.62f, 1f);

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        BuildOverlayCanvas();
        BuildPointerCanvas();
        BuildDialogueCanvas();

        _overlayCanvasGO.SetActive(false);
        _pointerCanvasGO.SetActive(false);
        _dialogueCanvasGO.SetActive(false);

        // Dedicated audio source for typewriter — lets us stop it mid-clip
        _typewriterSource = gameObject.AddComponent<AudioSource>();
        _typewriterSource.playOnAwake = false;
        _typewriterSource.loop = false;
        _typewriterSource.spatialBlend = 0f; // 2D
    }

    private void OnDestroy()
    {
        if (_overlayCanvasGO  != null) Destroy(_overlayCanvasGO);
        if (_pointerCanvasGO  != null) Destroy(_pointerCanvasGO);
        if (_dialogueCanvasGO != null) Destroy(_dialogueCanvasGO);
    }

    private void Update()
    {
        if (_arrowRT != null && _pointerCanvasGO.activeSelf)
        {
            _arrowBobPhase += Time.unscaledDeltaTime * 3f;
            float bob = Mathf.Sin(_arrowBobPhase) * 10f;
            _arrowRT.anchoredPosition = _arrowBasePos + new Vector2(0f, bob);
        }

        // Input cooldown (unscaled so it works while paused)
        if (_inputCooldown > 0f)
            _inputCooldown -= Time.unscaledDeltaTime;

        // Block dialogue input when any overlay UI is open or during flip/cooldown
        if (_dialogueActive && !_flipping && _inputCooldown <= 0f && !IsAnyOverlayOpen())
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetMouseButtonDown(0))
            {
                if (_typewriting)
                {
                    // Still typing — finish the line instantly
                    FinishTypewriter();
                }
                else
                {
                    // Line fully shown — advance to next
                    OnDialogueAdvance();
                }
            }
        }
    }

    // ── Public API ──────────────────────────────────────────────────────────

    public void ShowSpotlight(RectTransform target, GameObject targetCanvasGO = null)
    {
        RestoreBoostedCanvas();

        if (target == null)
        {
            _overlayCanvasGO.SetActive(false);
            _pointerCanvasGO.SetActive(false);
            return;
        }

        if (targetCanvasGO != null)
        {
            _boostedCanvas = targetCanvasGO.GetComponent<Canvas>();
            if (_boostedCanvas != null)
            {
                _boostedOriginalOrder = _boostedCanvas.sortingOrder;
                _boostedCanvas.sortingOrder = TargetOrder;
            }
        }

        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);

        var overlayRT = _overlayCanvasGO.GetComponent<RectTransform>();
        Vector2 min = ScreenToCanvasPos(overlayRT, RectTransformUtility.WorldToScreenPoint(null, corners[0]));
        Vector2 max = ScreenToCanvasPos(overlayRT, RectTransformUtility.WorldToScreenPoint(null, corners[2]));

        const float pad = 12f;
        min -= new Vector2(pad, pad);
        max += new Vector2(pad, pad);

        float refW = 1920f, refH = 1080f;

        _panelLeft.anchorMin = Vector2.zero;
        _panelLeft.anchorMax = Vector2.up;
        _panelLeft.offsetMin = Vector2.zero;
        _panelLeft.offsetMax = new Vector2(min.x, 0f);

        _panelRight.anchorMin = Vector2.zero;
        _panelRight.anchorMax = Vector2.up;
        _panelRight.offsetMin = new Vector2(max.x, 0f);
        _panelRight.offsetMax = new Vector2(refW, 0f);

        _panelBot.anchorMin = Vector2.zero;
        _panelBot.anchorMax = Vector2.zero;
        _panelBot.offsetMin = new Vector2(min.x, 0f);
        _panelBot.offsetMax = new Vector2(max.x, min.y);

        _panelTop.anchorMin = Vector2.zero;
        _panelTop.anchorMax = Vector2.zero;
        _panelTop.offsetMin = new Vector2(min.x, max.y);
        _panelTop.offsetMax = new Vector2(max.x, refH);

        _overlayCanvasGO.SetActive(true);
        ShowPointerAt(target);
    }

    public void HideSpotlight()
    {
        RestoreBoostedCanvas();
        _overlayCanvasGO.SetActive(false);
        _pointerCanvasGO.SetActive(false);
    }

    public void ShowDialogue(string[] lines, Action onComplete)
    {
        _currentLines = lines;
        _lineIndex = 0;
        _onDialogueComplete = onComplete;
        _dialogueActive = true;
        _inputCooldown = 0.25f; // prevent instant-advance on the same frame

        _dialogueCanvasGO.SetActive(true);
        ShowCurrentLine(playSound: true);
    }

    public void ShowDialogueWithSpotlight(string[] lines, RectTransform target,
        GameObject targetCanvasGO, Action onComplete)
    {
        ShowSpotlight(target, targetCanvasGO);
        ShowDialogue(lines, onComplete);
    }

    public void HideDialogue()
    {
        _dialogueActive = false;
        _typewriting = false;
        if (_typewriterCoroutine != null)
        {
            StopCoroutine(_typewriterCoroutine);
            _typewriterCoroutine = null;
        }
        StopTypewriterAudio();
        _dialogueCanvasGO.SetActive(false);
    }

    public void HideAll()
    {
        HideSpotlight();
        HideDialogue();
    }

    // ── Dialogue logic ──────────────────────────────────────────────────────

    private void ShowCurrentLine(bool playSound)
    {
        if (_lineIndex >= _currentLines.Length) return;

        if (playSound)
            PlayPageScroll();

        // Start typewriter coroutine
        if (_typewriterCoroutine != null)
            StopCoroutine(_typewriterCoroutine);
        _typewriterCoroutine = StartCoroutine(TypewriterRoutine(_currentLines[_lineIndex]));
    }

    private void StopTypewriterAudio()
    {
        if (_typewriterSource != null && _typewriterSource.isPlaying)
            _typewriterSource.Stop();
    }

    private void FinishTypewriter()
    {
        if (!_typewriting) return;
        if (_typewriterCoroutine != null)
            StopCoroutine(_typewriterCoroutine);
        _typewriterCoroutine = null;
        _typewriting = false;
        StopTypewriterAudio();

        // Show full text immediately
        if (_lineIndex < _currentLines.Length)
            _dialogueText.text = _currentLines[_lineIndex];
        _dialogueHint.text = "[Click / Space / Enter]";

        SFXManager.Instance?.PlayButtonClick();
    }

    private void StartTypewriterAudio()
    {
        if (typewriterClip == null || _typewriterSource == null) return;
        _typewriterSource.clip = typewriterClip;
        _typewriterSource.volume = typewriterVolume;
        _typewriterSource.loop = true;
        _typewriterSource.Play();
    }

    private IEnumerator TypewriterRoutine(string fullText)
    {
        _typewriting = true;
        _dialogueText.text = "";
        _dialogueHint.text = "";

        StartTypewriterAudio();

        for (int i = 0; i < fullText.Length; i++)
        {
            _dialogueText.text = fullText.Substring(0, i + 1);
            yield return new WaitForSecondsRealtime(typewriterSpeed);
        }

        StopTypewriterAudio();
        _typewriting = false;
        _typewriterCoroutine = null;
        _dialogueHint.text = "[Click / Space / Enter]";
    }

    private void OnDialogueAdvance()
    {
        if (!_dialogueActive) return;

        SFXManager.Instance?.PlayButtonClick();

        _lineIndex++;
        if (_lineIndex >= _currentLines.Length)
        {
            _dialogueActive = false;
            _dialogueCanvasGO.SetActive(false);
            _onDialogueComplete?.Invoke();
        }
        else
        {
            StartCoroutine(PageFlipThenShowLine());
        }
    }

    private IEnumerator PageFlipThenShowLine()
    {
        _flipping = true;

        // Play scroll sound
        PlayPageScroll();

        // Phase 1: page sweeps from right to left (cover current text)
        _pageFlipRT.gameObject.SetActive(true);
        float duration = 0.18f;
        float elapsed = 0f;

        // Start off-screen right, sweep left
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            // Pivot on right edge, scale X from 0 to 1
            _pageFlipRT.pivot = new Vector2(1f, 0.5f);
            _pageFlipRT.anchoredPosition = Vector2.zero;
            _pageFlipRT.localScale = new Vector3(t, 1f, 1f);
            _pageFlipImg.color = new Color(ColPageFlip.r, ColPageFlip.g, ColPageFlip.b, 0.95f);
            yield return null;
        }

        // Update text while covered
        ShowCurrentLine(playSound: false);

        // Phase 2: page continues sweeping, revealing new text
        elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            _pageFlipRT.pivot = new Vector2(0f, 0.5f);
            _pageFlipRT.anchoredPosition = Vector2.zero;
            _pageFlipRT.localScale = new Vector3(1f - t, 1f, 1f);
            float alpha = Mathf.Lerp(0.95f, 0f, t);
            _pageFlipImg.color = new Color(ColPageFlip.r, ColPageFlip.g, ColPageFlip.b, alpha);
            yield return null;
        }

        _pageFlipRT.gameObject.SetActive(false);
        _flipping = false;
    }

    private void PlayPageScroll()
    {
        if (pageScrollClip != null)
            SFXManager.Instance?.PlayUI(pageScrollClip, pageScrollVolume);
    }

    // ── Pointer arrow ───────────────────────────────────────────────────────

    private Vector2 _arrowBasePos;

    private void ShowPointerAt(RectTransform target)
    {
        if (target == null) { _pointerCanvasGO.SetActive(false); return; }

        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);

        var pointerCanvasRT = _pointerCanvasGO.GetComponent<RectTransform>();
        Vector2 screenMin = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        Vector2 screenMax = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        Vector2 screenCenter = (screenMin + screenMax) * 0.5f;

        Vector2 canvasPos = ScreenToCanvasPos(pointerCanvasRT, new Vector2(screenMin.x - 50f, screenCenter.y));
        _arrowBasePos = canvasPos;
        _arrowRT.anchoredPosition = canvasPos;
        _arrowBobPhase = 0f;

        _pointerCanvasGO.SetActive(true);
    }

    // ── Canvas construction ─────────────────────────────────────────────────

    private void BuildOverlayCanvas()
    {
        _overlayCanvasGO = new GameObject("TutorialOverlay_Canvas");
        _overlayCanvas = _overlayCanvasGO.AddComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = OverlayOrder;

        var scaler = _overlayCanvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _overlayCanvasGO.AddComponent<GraphicRaycaster>();

        Color dim = new Color(0f, 0f, 0f, 0.7f);

        _panelLeft  = MakeDimPanel("Left",  _overlayCanvasGO.transform, dim);
        _panelRight = MakeDimPanel("Right", _overlayCanvasGO.transform, dim);
        _panelBot   = MakeDimPanel("Bot",   _overlayCanvasGO.transform, dim);
        _panelTop   = MakeDimPanel("Top",   _overlayCanvasGO.transform, dim);
    }

    private RectTransform MakeDimPanel(string name, Transform parent, Color color)
    {
        var go = new GameObject($"Dim_{name}");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = true;
        return rt;
    }

    private void BuildPointerCanvas()
    {
        _pointerCanvasGO = new GameObject("TutorialPointer_Canvas");
        var canvas = _pointerCanvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = PointerOrder;

        var scaler = _pointerCanvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _arrowRT = MakeRT("Arrow", _pointerCanvasGO.transform,
            Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(48f, 48f));

        var arrowText = _arrowRT.gameObject.AddComponent<TextMeshProUGUI>();
        arrowText.font = font;
        arrowText.text = "\u25B6";
        arrowText.fontSize = 36f;
        arrowText.fontStyle = FontStyles.Bold;
        arrowText.alignment = TextAlignmentOptions.Center;
        arrowText.color = new Color(1f, 0.85f, 0.15f);
        arrowText.outlineWidth = 0.3f;
        arrowText.outlineColor = new Color32(0, 0, 0, 200);
        arrowText.raycastTarget = false;
    }

    private void BuildDialogueCanvas()
    {
        _dialogueCanvasGO = new GameObject("TutorialDialogue_Canvas");
        var canvas = _dialogueCanvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = DialogueOrder;

        var scaler = _dialogueCanvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // ── Parchment scroll panel ──────────────────────────────────────────
        _dialoguePanel = MakeRT("DialoguePanel", _dialogueCanvasGO.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 30f), new Vector2(1000f, 200f));

        // Outer edge (dark parchment border)
        var outerImg = _dialoguePanel.gameObject.AddComponent<Image>();
        outerImg.color = ColDarkEdge;

        // Parchment fill — inset from outer edge
        var parchmentRT = MakeRT("Parchment", _dialoguePanel,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        parchmentRT.offsetMin = new Vector2(4f, 4f);
        parchmentRT.offsetMax = new Vector2(-4f, -4f);
        var parchmentImg = parchmentRT.gameObject.AddComponent<Image>();
        parchmentImg.color = ColBg;
        parchmentImg.raycastTarget = false;

        // Decorative top/bottom ruled lines (like scroll edges)
        var topLine = MakeRT("TopLine", parchmentRT,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -18f), new Vector2(0f, 1.5f));
        topLine.offsetMin = new Vector2(20f, topLine.offsetMin.y);
        topLine.offsetMax = new Vector2(-20f, topLine.offsetMax.y);
        topLine.gameObject.AddComponent<Image>().color = new Color(0.55f, 0.42f, 0.22f, 0.4f);

        var botLine = MakeRT("BotLine", parchmentRT,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 18f), new Vector2(0f, 1.5f));
        botLine.offsetMin = new Vector2(20f, botLine.offsetMin.y);
        botLine.offsetMax = new Vector2(-20f, botLine.offsetMax.y);
        botLine.gameObject.AddComponent<Image>().color = new Color(0.55f, 0.42f, 0.22f, 0.4f);

        // Speaker name
        var speakerRT = MakeRT("Speaker", _dialoguePanel,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -10f), new Vector2(300f, 28f));
        _speakerText = speakerRT.gameObject.AddComponent<TextMeshProUGUI>();
        _speakerText.font = font;
        _speakerText.text = "The Chronicle";
        _speakerText.fontSize = 20f;
        _speakerText.fontStyle = FontStyles.Bold | FontStyles.Italic;
        _speakerText.alignment = TextAlignmentOptions.Left;
        _speakerText.color = ColSpeaker;
        _speakerText.outlineWidth = 0f;
        _speakerText.raycastTarget = false;

        // Dialogue text
        var textRT = MakeRT("Text", _dialoguePanel,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(28f, -44f), new Vector2(-56f, 110f));
        textRT.anchorMin = new Vector2(0f, 1f);
        textRT.anchorMax = new Vector2(1f, 1f);
        _dialogueText = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        _dialogueText.font = font;
        _dialogueText.fontSize = 34f;
        _dialogueText.fontStyle = FontStyles.Italic;
        _dialogueText.alignment = TextAlignmentOptions.TopLeft;
        _dialogueText.textWrappingMode = TextWrappingModes.Normal;
        _dialogueText.overflowMode = TextOverflowModes.Overflow;
        _dialogueText.color = ColText;
        _dialogueText.outlineWidth = 0f;
        _dialogueText.raycastTarget = false;

        // Click hint
        var hintRT = MakeRT("Hint", _dialoguePanel,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-18f, 22f), new Vector2(280f, 28f));
        _dialogueHint = hintRT.gameObject.AddComponent<TextMeshProUGUI>();
        _dialogueHint.font = font;
        _dialogueHint.fontSize = 16f;
        _dialogueHint.fontStyle = FontStyles.Italic;
        _dialogueHint.alignment = TextAlignmentOptions.Right;
        _dialogueHint.color = ColHint;
        _dialogueHint.raycastTarget = false;

        // ── Page flip overlay (hidden by default) ───────────────────────────
        _pageFlipRT = MakeRT("PageFlip", _dialoguePanel,
            Vector2.zero, Vector2.one, new Vector2(1f, 0.5f),
            Vector2.zero, Vector2.zero);
        _pageFlipRT.offsetMin = new Vector2(4f, 4f);
        _pageFlipRT.offsetMax = new Vector2(-4f, -4f);
        _pageFlipImg = _pageFlipRT.gameObject.AddComponent<Image>();
        _pageFlipImg.color = ColPageFlip;
        _pageFlipImg.raycastTarget = false;
        _pageFlipRT.gameObject.SetActive(false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool IsAnyOverlayOpen()
    {
        if (SettingsUI.Instance != null && SettingsUI.Instance.IsOpen) return true;
        if (_cachedMinimap == null) _cachedMinimap = FindAnyObjectByType<MinimapController>();
        if (_cachedMinimap != null && _cachedMinimap.IsExpanded) return true;
        // Grimoire and MergeRitual are NOT blocked — dialogue should advance over them
        return false;
    }

    private void RestoreBoostedCanvas()
    {
        if (_boostedCanvas != null)
        {
            _boostedCanvas.sortingOrder = _boostedOriginalOrder;
            _boostedCanvas = null;
        }
    }

    private static Vector2 ScreenToCanvasPos(RectTransform canvasRT, Vector2 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRT, screenPos, null, out Vector2 localPos);
        return localPos;
    }

    private static RectTransform MakeRT(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }
}
