using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Tutorial completion popup — parchment-styled panel with a "Return to Main Menu" button.
/// Matches the SettingsUI parchment aesthetic.
///
/// Setup: Attach to a GameObject in the Tutorial scene. Assign font.
/// TutorialDirector calls Show() when the tutorial is complete.
/// </summary>
public class TutorialCompleteUI : MonoBehaviour
{
    [Header("Font")]
    [SerializeField] private TMP_FontAsset font;

    private GameObject _canvasGO;

    public void Show()
    {
        if (_canvasGO != null) return;
        PauseManager.Pause();
        Build();
    }

    private void OnDestroy()
    {
        if (_canvasGO != null) Destroy(_canvasGO);
    }

    private void Build()
    {
        _canvasGO = new GameObject("TutorialComplete_Canvas");
        var canvas = _canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;

        var scaler = _canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGO.AddComponent<GraphicRaycaster>();

        // Dark backdrop
        var backdropRT = MakeRT("Backdrop", _canvasGO.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        backdropRT.offsetMin = Vector2.zero;
        backdropRT.offsetMax = Vector2.zero;
        var backdropImg = backdropRT.gameObject.AddComponent<Image>();
        backdropImg.color = new Color(0f, 0f, 0f, 0.78f);

        // Parchment panel
        const float panelW = 600f;
        const float panelH = 340f;

        var panelRT = MakeRT("Panel", _canvasGO.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(panelW, panelH));
        var panelImg = panelRT.gameObject.AddComponent<Image>();
        panelImg.color = new Color(0.16f, 0.12f, 0.08f, 0.97f);

        // Gold border
        var borderRT = MakeRT("Border", panelRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        borderRT.offsetMin = new Vector2(4f, 4f);
        borderRT.offsetMax = new Vector2(-4f, -4f);
        var borderImg = borderRT.gameObject.AddComponent<Image>();
        borderImg.color = new Color(0.85f, 0.65f, 0.15f, 0.6f);
        borderImg.raycastTarget = false;

        // Inner fill
        var innerRT = MakeRT("Inner", borderRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        innerRT.offsetMin = new Vector2(3f, 3f);
        innerRT.offsetMax = new Vector2(-3f, -3f);
        var innerImg = innerRT.gameObject.AddComponent<Image>();
        innerImg.color = new Color(0.14f, 0.10f, 0.06f, 0.97f);
        innerImg.raycastTarget = false;

        // Title
        MakeText("Title", panelRT,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -45f), new Vector2(500f, 50f),
            "Tutorial Complete", 36f, new Color(0.85f, 0.65f, 0.15f),
            TextAlignmentOptions.Center);

        // Subtitle
        MakeText("Subtitle", panelRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 20f), new Vector2(500f, 60f),
            "You survived the first lesson.\nThe real dungeon won't be this kind.",
            16f, new Color(0.93f, 0.87f, 0.72f, 0.8f),
            TextAlignmentOptions.Center);

        // Return to Main Menu button
        var btnRT = MakeRT("ReturnBtn", panelRT,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 50f), new Vector2(300f, 56f));
        var btnImg = btnRT.gameObject.AddComponent<Image>();
        btnImg.color = new Color(0.55f, 0.12f, 0.08f);
        var btn = btnRT.gameObject.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(OnReturnToMenu);
        btnRT.gameObject.AddComponent<UIButtonHover>();

        MakeText("BtnText", btnRT,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            "Return to Main Menu", 20f, Color.white,
            TextAlignmentOptions.Center);

        // Animate in
        StartCoroutine(UIPanelAnimator.AnimateIn(panelRT));
    }

    private void OnReturnToMenu()
    {
        PauseManager.Reset();

        // Reset all persistent singletons
        Grimoire.Instance?.ResetAll();
        UltimateAbility.Instance?.ResetGauge();

        // Destroy lingering gameplay objects (projectiles, enemies, orbitals, zones)
        foreach (var proj in Object.FindObjectsByType<ProjectileHandler>(FindObjectsSortMode.None))
            if (proj != null) Destroy(proj.gameObject);
        foreach (var orb in Object.FindObjectsByType<OrbitalMotion>(FindObjectsSortMode.None))
            if (orb != null) Destroy(orb.gameObject);
        foreach (var zone in Object.FindObjectsByType<LingeringZone>(FindObjectsSortMode.None))
            if (zone != null) Destroy(zone.gameObject);
        foreach (var enemy in Object.FindObjectsByType<EnemyBase>(FindObjectsSortMode.None))
            if (enemy != null) Destroy(enemy.gameObject);

        // Destroy the persistent Player entirely so the next scene starts fresh.
        // The Tutorial scene has its own Player prefab instance in the hierarchy.
        // The Dungeon flow creates one via IntroCutscene.
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            // Destroy the PlayerHUD canvas (root-level GO, won't auto-destroy)
            var hud = playerObj.GetComponent<PlayerHUD>();
            if (hud != null && hud.CanvasGO != null)
                Destroy(hud.CanvasGO);

            Destroy(playerObj);
        }

        // Destroy Grimoire singleton so a fresh one is created in the next scene
        if (Grimoire.Instance != null)
            Destroy(Grimoire.Instance.gameObject);

        SceneManager.LoadScene("Menu");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private TMP_Text MakeText(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size,
        string text, float fontSize, Color color, TextAlignmentOptions align)
    {
        var rt = MakeRT(name, parent, anchorMin, anchorMax, pivot, pos, size);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color = color;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = new Color32(0, 0, 0, 180);
        tmp.raycastTarget = false;
        tmp.text = text;
        return tmp;
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
