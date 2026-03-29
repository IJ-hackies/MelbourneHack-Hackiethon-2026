using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fully scripted intro cutscene — no Timeline required.
///
/// SEQUENCE:
///   1. Title card fades in/out on black
///   2. World fades in — Grimoire glows purple in the distance
///   3. Player walks north; chronicle text typewriters in; Grimoire glows brighter as player approaches
///   4. Player stops in front of Grimoire; it floats upward
///   5. Mid-float: 8 enemies spawn at screen edges from all compass directions
///   6. Camera zooms out to reveal the ring closing in
///   7. Enemies walk to a surrounding ring, brief standoff
///   8. Camera zooms in; all enemies charge simultaneously
///   9. Slow freeze-frame → white flash → black → load gameplay scene
///
/// SETUP:
///   1. Create scene "IntroCutscene"
///   2. Add empty GameObject, attach this script
///   3. Assign playerPrefab, enemyPrefabs[8] (N NE E SE S SW W NW), grimoireSprite in Inspector
///   4. Set gameplaySceneName to your game scene (default "StageTest")
///   5. enemyAnimPrefixes defaults are already set for the expected enemy order
/// </summary>
public class IntroCutscene : MonoBehaviour
{
    // ── Prefab References ────────────────────────────────────────────────────
    [Header("Character Prefabs")]
    [SerializeField] private GameObject playerPrefab;

    [Tooltip("8 slots — N, NE, E, SE, S, SW, W, NW. Expected: DragonNewt, Vampire, IceWizard, Zombie, Skeleton, Ghost, FireWizard, EvilPaladin")]
    [SerializeField] private GameObject[] enemyPrefabs = new GameObject[8];

    [Header("Enemy Walk Animation Prefixes")]
    [Tooltip("One prefix per enemyPrefabs slot. Standard enemies: 'walk'. DragonNewt: 'dn_walk'. EvilPaladin: 'ep_walk'.")]
    [SerializeField] private string[] enemyAnimPrefixes = {
        "dn_walk",  // N  — DragonNewt
        "walk",     // NE — Vampire
        "walk",     // E  — IceWizard
        "walk",     // SE — Zombie
        "walk",     // S  — Skeleton
        "walk",     // SW — Ghost
        "walk",     // W  — FireWizard
        "ep_walk",  // NW — EvilPaladin
    };

    [Header("Grimoire")]
    [Tooltip("Drag Assets/Art/Sprites/Spell/StarterSpell here")]
    [SerializeField] private Sprite grimoireSprite;

    [Header("Lighting")]
    [SerializeField] private GameObject globalLightPrefab;

    [Header("Camera")]
    [SerializeField] private float baseCameraSize = 5f;

    [Header("UI")]
    [SerializeField] private TMP_FontAsset         uiFont;
    [SerializeField] private Canvas                cutsceneCanvas;
    [SerializeField] private Image                 fadeImage;
    [SerializeField] private TMP_Text              chronicleText;
    [SerializeField] private TMP_Text              titleText;
    [SerializeField] private UnityEngine.UI.Button skipButton;
    [SerializeField] private TMP_Text              skipText;

    [Header("Narrative")]
    [SerializeField] [TextArea(3, 6)]
    private string chronicleMessage =
        "Another soul stumbles into the dark.\nThe Grimoire stirs... it has been waiting.\nLet us see what you are made of.";
    [SerializeField] private string titleMessage = "EVERCHANGING GRIMOIRE";

    [Header("Scene Transition")]
    [SerializeField] private string gameplaySceneName = "StageTest";

    // ── Runtime state ────────────────────────────────────────────────────────
    private Camera                mainCam;
    private CinemachineBrain      cinemachineBrain;
    private GameObject            playerObj;
    private List<GameObject>      spawnedEnemies = new();
    private Light2D               globalLight;
    private bool                  skipping;

    private GameObject            grimoireObj;
    private Light2D               grimoireLight;
    private ParticleSystem        grimoireParticles;

    private const float GrimoireGlowStartDist = 5.5f;
    private const float RingRadius            = 7.5f;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        SetupCamera();
        SetupLighting();
        SetupUI();
        StartCoroutine(RunCutscene());
    }

    private void Update() { } // skip is button-only

    // ═════════════════════════════════════════════════════════════════════════
    //  SETUP
    // ═════════════════════════════════════════════════════════════════════════

    private void SetupCamera()
    {
        var camObj = new GameObject("CutsceneCamera");
        mainCam = camObj.AddComponent<Camera>();
        mainCam.orthographic     = true;
        mainCam.orthographicSize = baseCameraSize;
        mainCam.backgroundColor  = Color.black;
        mainCam.clearFlags       = CameraClearFlags.SolidColor;
        mainCam.transform.position = new Vector3(0f, 0f, -10f);
        camObj.tag = "MainCamera";
        camObj.AddComponent<AudioListener>();

        cinemachineBrain = camObj.AddComponent<CinemachineBrain>();
        cinemachineBrain.m_DefaultBlend = new CinemachineBlendDefinition(
            CinemachineBlendDefinition.Style.EaseInOut, 1.2f);
    }

    private void SetupLighting()
    {
        if (globalLightPrefab != null)
        {
            globalLight = Instantiate(globalLightPrefab).GetComponent<Light2D>();
        }
        else
        {
            var go = new GameObject("GlobalLight");
            globalLight = go.AddComponent<Light2D>();
            globalLight.lightType = Light2D.LightType.Global;
        }
        globalLight.intensity = 0f;
    }

    private void SetupUI()
    {
        if (cutsceneCanvas == null)
        {
            var cGO = new GameObject("CutsceneCanvas");
            cutsceneCanvas = cGO.AddComponent<Canvas>();
            cutsceneCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            cutsceneCanvas.sortingOrder = 100;
            var scaler = cGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight  = 0.5f;
            cGO.AddComponent<GraphicRaycaster>();
        }

        if (fadeImage == null)
        {
            var go = new GameObject("FadeOverlay");
            go.transform.SetParent(cutsceneCanvas.transform, false);
            fadeImage = go.AddComponent<Image>();
            fadeImage.raycastTarget = false;
            var rt = fadeImage.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
        fadeImage.color = Color.black;

        if (chronicleText == null)
            chronicleText = MakeUIText("ChronicleText", 28, FontStyles.Italic,
                new Vector2(0.1f, 0.08f), new Vector2(0.9f, 0.48f));
        chronicleText.text  = "";
        chronicleText.color = new Color(0.85f, 0.75f, 0.55f, 0f);

        if (titleText == null)
            titleText = MakeUIText("TitleText", 52, FontStyles.Bold,
                new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.65f));
        titleText.text  = "";
        titleText.color = new Color(1f, 0.9f, 0.6f, 0f);

        // Skip button — top-right corner
        if (skipButton == null)
        {
            var btnGO = new GameObject("SkipButton");
            btnGO.transform.SetParent(cutsceneCanvas.transform, false);

            var btnRT              = btnGO.AddComponent<RectTransform>();
            btnRT.anchorMin        = new Vector2(1f, 1f);
            btnRT.anchorMax        = new Vector2(1f, 1f);
            btnRT.pivot            = new Vector2(1f, 1f);
            btnRT.anchoredPosition = new Vector2(-20f, -20f);
            btnRT.sizeDelta        = new Vector2(120f, 40f);

            var btnImg             = btnGO.AddComponent<Image>();
            btnImg.color           = new Color(0.1f, 0.1f, 0.1f, 0.7f);

            skipButton             = btnGO.AddComponent<UnityEngine.UI.Button>();
            skipButton.targetGraphic = btnImg;
            skipButton.onClick.AddListener(() => skipping = true);

            skipText = MakeUIText("SkipLabel", 18, FontStyles.Normal, Vector2.zero, Vector2.one);
            skipText.transform.SetParent(btnGO.transform, false);
            var labelRT       = skipText.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
        }

        skipText.text  = "SKIP";
        skipText.color = Color.white;
    }

    private TMP_Text MakeUIText(string name, float size, FontStyles style, Vector2 aMin, Vector2 aMax)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(cutsceneCanvas.transform, false);
        var t   = go.AddComponent<TextMeshProUGUI>();
        t.font  = uiFont;
        t.fontSize          = size;
        t.fontStyle         = style;
        t.alignment         = TextAlignmentOptions.Center;
        t.textWrappingMode  = TextWrappingModes.Normal;
        t.overflowMode      = TextOverflowModes.Overflow;
        t.raycastTarget     = false;
        var rt  = t.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return t;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CUTSCENE SEQUENCE
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator RunCutscene()
    {
        yield return new WaitForSeconds(0.3f);

        // ── ACT 1: Title card ────────────────────────────────────────────────
        // Start empty at full alpha so TypewriterReveal builds up with no pre-flash
        titleText.text  = "";
        titleText.color = new Color(titleText.color.r, titleText.color.g, titleText.color.b, 1f);
        yield return TypewriterReveal(titleText, titleMessage, 0.07f);
        yield return Wait(1.5f);
        yield return FadeTextAlpha(titleText, 1f, 0f, 1.0f);

        if (skipping) { yield return SkipToGameplay(); yield break; }

        // ── ACT 2: World fades in — Grimoire glows purple in the distance ────
        SpawnGrimoire(new Vector3(0f, 0.2f, 0f));
        SpawnPlayer(new Vector3(0f, -4f, 0f));
        var playerVcam = CreateVcam("PlayerCam", playerObj.transform, baseCameraSize);

        yield return FadeOverlay(1f, 0f, 1.5f);
        yield return AnimateGlobalLight(0f, 0.06f, 1.5f);
        yield return Wait(0.4f);

        if (skipping) { yield return SkipToGameplay(); yield break; }

        // ── ACT 3: Player walks north; chronicle narrates; glow intensifies ──
        Coroutine glowRoutine = StartCoroutine(UpdateGrimoireGlow(playerObj));

        // Start empty at full alpha — typewriter reveals with no pre-flash
        chronicleText.text  = "";
        chronicleText.color = new Color(chronicleText.color.r, chronicleText.color.g, chronicleText.color.b, 1f);

        // Walk and typewriter run concurrently — walk pace tuned to match text reveal
        Coroutine typeRoutine = StartCoroutine(TypewriterReveal(chronicleText, chronicleMessage, 0.065f));
        yield return MoveCharacter(playerObj, new Vector3(0f, -4f), new Vector3(0f, -1.2f), 8f, "walk_north");
        // Player arrives — idle while remaining text finishes typewriting
        PlayAnimation(playerObj, "idle_north");
        yield return typeRoutine;

        StopCoroutine(glowRoutine);
        // Lock glow at max
        if (grimoireLight != null) grimoireLight.intensity = 1.8f;

        if (skipping) { yield return SkipToGameplay(); yield break; }

        // ── ACT 4: Chronicle fades; Grimoire floats up ───────────────────────
        yield return Wait(0.5f);
        yield return FadeTextAlpha(chronicleText, 1f, 0f, 0.7f);

        Coroutine floatRoutine = StartCoroutine(AnimateGrimoireFloat());

        // Mid-float pause before enemies appear
        yield return new WaitForSeconds(0.85f);

        if (skipping) { yield return SkipToGameplay(); yield break; }

        // ── ACT 5: 8 enemies spawn just outside camera edges ─────────────────
        float halfH = baseCameraSize + 1.8f;
        float halfW = baseCameraSize * (mainCam != null ? mainCam.aspect : 1.78f) + 1.8f;
        Vector3 pPos = playerObj.transform.position;

        // Compass order: N NE E SE S SW W NW  (matches enemyPrefabs slots)
        Vector3[] offsets = {
            new( 0,      halfH,  0),   // N
            new( halfW,  halfH,  0),   // NE
            new( halfW,  0,      0),   // E
            new( halfW, -halfH,  0),   // SE
            new( 0,     -halfH,  0),   // S
            new(-halfW, -halfH,  0),   // SW
            new(-halfW,  0,      0),   // W
            new(-halfW,  halfH,  0),   // NW
        };

        spawnedEnemies.Clear();
        int count = Mathf.Min(enemyPrefabs != null ? enemyPrefabs.Length : 0, 8);
        for (int i = 0; i < count; i++)
        {
            if (enemyPrefabs[i] == null) continue;
            spawnedEnemies.Add(SpawnEnemy(enemyPrefabs[i], pPos + offsets[i]));
        }

        // Camera zooms out to reveal all directions
        yield return AnimateCameraSize(playerVcam, baseCameraSize, baseCameraSize + 3.5f, 1.6f);
        yield return floatRoutine;

        if (skipping) { yield return SkipToGameplay(); yield break; }

        // ── ACT 6: All enemies walk in to surrounding ring ───────────────────
        var ringRoutines = new List<Coroutine>();
        for (int i = 0; i < spawnedEnemies.Count; i++)
        {
            var enemy = spawnedEnemies[i];
            if (enemy == null) continue;

            Vector3 from    = enemy.transform.position;
            Vector3 toDir   = (pPos - from).normalized;
            Vector3 ringPos = pPos - toDir * RingRadius;  // stop RingRadius units AWAY from player

            string prefix  = GetAnimPrefix(enemy);
            string dir     = GetWalkDirection(from, pPos);
            ringRoutines.Add(StartCoroutine(MoveCharacter(enemy, from, ringPos, 4.0f, prefix + "_" + dir)));
        }
        foreach (var r in ringRoutines) yield return r;

        if (skipping) { yield return SkipToGameplay(); yield break; }

        // Brief standoff — enemies face the player
        yield return Wait(0.55f);

        // ── ACT 7: Camera zooms in; all enemies charge simultaneously ────────
        StartCoroutine(AnimateCameraSize(
            playerVcam, baseCameraSize + 3.5f, baseCameraSize - 0.5f, 1.1f));

        for (int i = 0; i < spawnedEnemies.Count; i++)
        {
            var enemy = spawnedEnemies[i];
            if (enemy == null) continue;

            Vector3 from      = enemy.transform.position;
            Vector3 toDir     = (pPos - from).normalized;
            Vector3 chargeEnd = pPos - toDir * 3.5f;  // stop 3.5 units short of player
            string prefix  = GetAnimPrefix(enemy);
            string dir     = GetWalkDirection(from, pPos);
            StartCoroutine(MoveCharacter(enemy, from, chargeEnd, 2.2f, prefix + "_" + dir));
        }

        // Wait until enemies are mid-charge (~40 % through) then trigger slow-mo
        yield return new WaitForSeconds(0.9f);

        // ── ACT 8: Slow freeze → white flash → black → gameplay ──────────────
        yield return SlowFreezeAndFlash();
        SceneManager.LoadScene(gameplaySceneName);
    }

    private IEnumerator SkipToGameplay()
    {
        Time.timeScale = 1f;
        yield return FadeOverlayUnscaled(fadeImage.color.a, 1f, Color.black, 0.4f);
        SceneManager.LoadScene(gameplaySceneName);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  GRIMOIRE
    // ═════════════════════════════════════════════════════════════════════════

    private void SpawnGrimoire(Vector3 position)
    {
        grimoireObj = new GameObject("Grimoire");
        grimoireObj.transform.position = position;

        if (grimoireSprite != null)
        {
            var sr              = grimoireObj.AddComponent<SpriteRenderer>();
            sr.sprite           = grimoireSprite;
            sr.sortingLayerName = "Entities";
            sr.sortingOrder     = 50;
            grimoireObj.transform.localScale = Vector3.one * 0.5f;
        }

        // Purple point light — dim at first, intensifies as player approaches
        grimoireLight                       = grimoireObj.AddComponent<Light2D>();
        grimoireLight.lightType             = Light2D.LightType.Point;
        grimoireLight.color                 = new Color(0.55f, 0.15f, 1f);
        grimoireLight.intensity             = 0.25f;
        grimoireLight.pointLightOuterRadius = 2.0f;
        grimoireLight.pointLightInnerRadius = 0.4f;

        // Purple aura particles — sparse at first
        var psGO              = new GameObject("GrimoireAura");
        psGO.transform.SetParent(grimoireObj.transform, false);
        grimoireParticles     = psGO.AddComponent<ParticleSystem>();
        grimoireParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr              = psGO.GetComponent<ParticleSystemRenderer>();
        psr.sortingLayerName = "Entities";
        psr.sortingOrder     = 51;
        ApplyParticleMaterial(psr);

        var main             = grimoireParticles.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.7f, 0.2f, 1f, 0.9f),
                                   new Color(0.95f, 0.45f, 1f, 0.6f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.2f;  // drift upward
        main.maxParticles    = 60;

        var emission         = grimoireParticles.emission;
        emission.rateOverTime = 2f;

        var shape            = grimoireParticles.shape;
        shape.enabled        = true;
        shape.shapeType      = ParticleSystemShapeType.Sphere;
        shape.radius         = 0.2f;

        var col              = grimoireParticles.colorOverLifetime;
        col.enabled          = true;
        var g                = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.8f, 0.3f, 1f), 0f),
                    new GradientColorKey(new Color(0.5f, 0.1f, 0.8f), 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        grimoireParticles.Play();
    }

    /// <summary>Runs each frame; ramps glow + particle density as player closes in.</summary>
    private IEnumerator UpdateGrimoireGlow(GameObject player)
    {
        while (player != null && grimoireObj != null)
        {
            float dist = Vector3.Distance(player.transform.position, grimoireObj.transform.position);
            float t    = 1f - Mathf.Clamp01(dist / GrimoireGlowStartDist);

            if (grimoireLight != null)
            {
                grimoireLight.intensity             = Mathf.Lerp(0.25f, 1.8f, t);
                grimoireLight.pointLightOuterRadius = Mathf.Lerp(2.0f, 4.5f, t);
            }

            if (grimoireParticles != null)
            {
                var em = grimoireParticles.emission;
                em.rateOverTime = Mathf.Lerp(2f, 20f, t);
            }

            yield return null;
        }
    }

    private IEnumerator AnimateGrimoireFloat()
    {
        if (grimoireObj == null) yield break;

        Vector3 start    = grimoireObj.transform.position;
        Vector3 end      = start + new Vector3(0f, 2.2f, 0f);
        float   duration = 2.6f, elapsed = 0f;

        while (elapsed < duration)
        {
            if (skipping) { grimoireObj.transform.position = end; yield break; }
            elapsed += Time.deltaTime;
            float t = EaseInOutCubic(Mathf.Clamp01(elapsed / duration));
            grimoireObj.transform.position = Vector3.Lerp(start, end, t);

            // Pulse the glow while floating
            if (grimoireLight != null)
                grimoireLight.intensity = 1.8f + Mathf.Sin(elapsed * 5f) * 0.3f;

            yield return null;
        }
        grimoireObj.transform.position = end;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  FREEZE FRAME
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator SlowFreezeAndFlash()
    {
        // Gradually slow time to a freeze over a long, dramatic ramp
        float elapsed = 0f, duration = 2.5f;
        while (elapsed < duration)
        {
            elapsed    += Time.unscaledDeltaTime;
            Time.timeScale = Mathf.Lerp(1f, 0f, EaseInOutCubic(Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(0.3f);  // brief freeze before flash

        // White flash — snappy
        yield return FadeOverlayUnscaled(0f, 1f, Color.white, 0.18f);
        yield return new WaitForSecondsRealtime(0.4f);

        // Bleed into black
        yield return FadeOverlayUnscaled(1f, 1f, Color.black, 0.55f);
        yield return new WaitForSecondsRealtime(0.5f);

        Time.timeScale = 1f;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  SPAWNING
    // ═════════════════════════════════════════════════════════════════════════

    private void SpawnPlayer(Vector3 position)
    {
        if (playerPrefab == null) { Debug.LogError("[IntroCutscene] playerPrefab not assigned."); return; }

        playerObj = Instantiate(playerPrefab, position, Quaternion.identity);
        playerObj.name = "CutscenePlayer";

        DisableComp<PlayerMovement>(playerObj);
        DisableComp<Health>(playerObj);
        DisableComp<PlayerHitEffect>(playerObj);
        DisableComp<PlayerStatusEffects>(playerObj);
        DisableRB(playerObj);

        foreach (var e in playerObj.GetComponentsInChildren<SpellExecutor>())
            e.enabled = false;

        var pa = playerObj.GetComponentInChildren<PlayerAnimator>();
        if (pa != null) pa.enabled = false;
    }

    private GameObject SpawnEnemy(GameObject prefab, Vector3 position)
    {
        var go   = Instantiate(prefab, position, Quaternion.identity);
        go.name  = prefab.name.Replace("Variant", "").Trim() + "_Cutscene";

        DisableComp<EnemyBase>(go);
        DisableComp<Health>(go);
        DisableComp<EnemyHealthBar>(go);
        DisableComp<EnemyRegen>(go);
        DisableComp<Pathfinding.Seeker>(go);
        DisableRB(go);

        // Keep Animator alive for walk clips
        var anim = go.GetComponentInChildren<Animator>();
        if (anim != null) anim.enabled = true;

        return go;
    }

    private static void DisableComp<T>(GameObject obj) where T : Behaviour
    {
        var c = obj.GetComponentInChildren<T>();
        if (c != null) c.enabled = false;
    }

    private static void DisableRB(GameObject obj)
    {
        var rb = obj.GetComponentInChildren<Rigidbody2D>();
        if (rb != null) rb.simulated = false;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LIGHTING HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator AnimateGlobalLight(float from, float to, float duration)
    {
        if (globalLight == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (skipping) { globalLight.intensity = to; yield break; }
            elapsed += Time.deltaTime;
            globalLight.intensity = Mathf.SmoothStep(from, to, elapsed / duration);
            yield return null;
        }
        globalLight.intensity = to;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CAMERA
    // ═════════════════════════════════════════════════════════════════════════

    private CinemachineVirtualCamera CreateVcam(string name, Transform follow, float size)
    {
        var go   = new GameObject(name);
        var vcam = go.AddComponent<CinemachineVirtualCamera>();
        vcam.Follow = follow;
        vcam.m_Lens.OrthographicSize = size;
        vcam.m_Lens.NearClipPlane    = -10f;

        var body             = vcam.AddCinemachineComponent<CinemachineFramingTransposer>();
        body.m_LookaheadTime = 0f;
        body.m_XDamping      = 1.2f;
        body.m_YDamping      = 1.2f;
        body.m_DeadZoneWidth = body.m_DeadZoneHeight = 0f;
        return vcam;
    }

    private IEnumerator AnimateCameraSize(CinemachineVirtualCamera vcam, float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (skipping) { vcam.m_Lens.OrthographicSize = to; yield break; }
            elapsed += Time.deltaTime;
            vcam.m_Lens.OrthographicSize = Mathf.Lerp(from, to, EaseInOutCubic(elapsed / duration));
            yield return null;
        }
        vcam.m_Lens.OrthographicSize = to;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CHARACTER MOVEMENT & ANIMATION
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator MoveCharacter(GameObject character, Vector3 from, Vector3 to,
                                      float duration, string walkClip)
    {
        if (character == null) yield break;

        var anim = character.GetComponentInChildren<Animator>();
        if (anim != null) anim.Play(walkClip);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (skipping || character == null) yield break;
            elapsed += Time.deltaTime;
            character.transform.position = Vector3.Lerp(from, to,
                EaseInOutCubic(Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        if (character != null) character.transform.position = to;
    }

    private static void PlayAnimation(GameObject character, string clip)
    {
        if (character == null) return;
        var anim = character.GetComponentInChildren<Animator>();
        if (anim != null) anim.Play(clip);
    }

    /// <summary>Derives the walk animation prefix from the enemy GameObject name.</summary>
    private static string GetAnimPrefix(GameObject enemy)
    {
        string n = enemy.name.ToLower();
        if (n.Contains("dragonnewt")) return "dn_walk";
        if (n.Contains("evilpaladin")) return "ep_walk";
        if (n.Contains("firewizard"))  return "fw_walk";
        if (n.Contains("icewizard"))   return "iw_walk";
        if (n.Contains("vampire"))     return "vampire_walk";
        if (n.Contains("zombie"))      return "zombie_walk";
        if (n.Contains("skeleton"))    return "skeleton_walk";
        if (n.Contains("ghost"))       return "ghost_walk";
        return "walk";
    }

    /// <summary>Returns an 8-direction string (e.g. "south_west") from a world-space vector.</summary>
    private static string GetWalkDirection(Vector3 from, Vector3 to)
    {
        Vector2 dir   = ((Vector2)(to - from)).normalized;
        float   angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (angle < 0) angle += 360f;

        if (angle >= 337.5f || angle < 22.5f) return "east";
        if (angle < 67.5f)                    return "north_east";
        if (angle < 112.5f)                   return "north";
        if (angle < 157.5f)                   return "north_west";
        if (angle < 202.5f)                   return "west";
        if (angle < 247.5f)                   return "south_west";
        if (angle < 292.5f)                   return "south";
        return "south_east";
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  SCREEN EFFECTS
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator FadeOverlay(float fromAlpha, float toAlpha, float duration)
    {
        if (fadeImage == null) yield break;
        float elapsed = 0f;
        Color c = fadeImage.color;
        while (elapsed < duration)
        {
            if (skipping) { c.a = toAlpha; fadeImage.color = c; yield break; }
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(fromAlpha, toAlpha, elapsed / duration);
            fadeImage.color = c;
            yield return null;
        }
        c.a = toAlpha;
        fadeImage.color = c;
    }

    /// <summary>Unscaled-time fade — used during timeScale=0 freeze frame.</summary>
    private IEnumerator FadeOverlayUnscaled(float fromAlpha, float toAlpha, Color targetColor, float duration)
    {
        if (fadeImage == null) yield break;
        fadeImage.color = new Color(targetColor.r, targetColor.g, targetColor.b, fromAlpha);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(fromAlpha, toAlpha, elapsed / duration);
            fadeImage.color = new Color(targetColor.r, targetColor.g, targetColor.b, a);
            yield return null;
        }
        fadeImage.color = new Color(targetColor.r, targetColor.g, targetColor.b, toAlpha);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  TEXT EFFECTS
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator TypewriterReveal(TMP_Text textComp, string fullText, float charDelay)
    {
        textComp.text = "";
        for (int i = 0; i < fullText.Length; i++)
        {
            if (skipping) { textComp.text = fullText; yield break; }
            textComp.text = fullText.Substring(0, i + 1);
            float delay = charDelay;
            char ch = fullText[i];
            if (ch == '.' || ch == '!' || ch == '?') delay *= 6f;
            else if (ch == ',')                       delay *= 3f;
            else if (ch == '\n')                      delay *= 4f;
            yield return new WaitForSeconds(delay);
        }
    }

    private IEnumerator FadeTextAlpha(TMP_Text textComp, float from, float to, float duration)
    {
        if (textComp == null) yield break;
        float elapsed = 0f;
        Color c = textComp.color;
        while (elapsed < duration)
        {
            if (skipping) { c.a = to; textComp.color = c; yield break; }
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(from, to, elapsed / duration);
            textComp.color = c;
            yield return null;
        }
        c.a = to;
        textComp.color = c;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  AMBIENT DUST (public — call from Inspector event or another script)
    // ═════════════════════════════════════════════════════════════════════════

    public void SpawnAmbientDust()
    {
        var go = new GameObject("AmbientDust");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main             = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(4f, 8f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(1f, 0.9f, 0.7f, 0.15f),
                                   new Color(1f, 1f,   1f,   0.25f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 80;
        main.gravityModifier = -0.01f;

        var emission         = ps.emission;
        emission.rateOverTime = 15f;

        var shape            = ps.shape;
        shape.shapeType      = ParticleSystemShapeType.Box;
        shape.scale          = new Vector3(18f, 12f, 0.1f);

        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.x = new ParticleSystem.MinMaxCurve(-0.1f,  0.1f);
        vol.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.08f);
        vol.z = new ParticleSystem.MinMaxCurve( 0f,    0f);

        var sol     = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.2f, 1f),
            new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));

        var col     = ps.colorOverLifetime;
        col.enabled = true;
        var g       = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        ApplyParticleMaterial(ps.GetComponent<ParticleSystemRenderer>());
        ps.Play();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UTILITY
    // ═════════════════════════════════════════════════════════════════════════

    private static void ApplyParticleMaterial(ParticleSystemRenderer psr)
    {
        string[] candidates = {
            "Universal Render Pipeline/Particles/Unlit",
            "Particles/Standard Unlit",
            "Legacy Shaders/Particles/Alpha Blended Premultiply",
            "Sprites/Default",
        };
        Shader shader = null;
        foreach (var s in candidates) { shader = Shader.Find(s); if (shader != null) break; }
        if (shader == null) return;

        var mat = new Material(shader) { color = Color.white };
        var knob = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
        if (knob != null) mat.mainTexture = knob.texture;

        psr.material   = mat;
        psr.renderMode = ParticleSystemRenderMode.Billboard;
    }

    private IEnumerator Wait(float seconds)
    {
        if (skipping) yield break;
        yield return new WaitForSeconds(seconds);
    }

    private static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
}
