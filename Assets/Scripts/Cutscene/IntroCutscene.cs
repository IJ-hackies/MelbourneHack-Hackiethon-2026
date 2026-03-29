using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fully scripted intro cutscene — no Timeline required.
/// Attach to an empty GameObject in the cutscene scene.
/// Uses existing player/enemy prefabs, URP 2D lighting, Cinemachine, and particle effects.
///
/// SETUP:
///   1. Create a new scene "IntroCutscene"
///   2. Add an empty GameObject, attach this script
///   3. Wire the prefab fields in the Inspector (Player, 3–4 enemy prefabs, lamp prefabs)
///   4. Wire the UI fields (canvas, text, fade image)
///   5. Set gameplaySceneName to "StageTest"
///   6. Hit Play — the cutscene runs automatically
/// </summary>
public class IntroCutscene : MonoBehaviour
{
    // ── Prefab References ────────────────────────────────────────────────────
    [Header("Character Prefabs")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject[] enemyPrefabs;   // 3–4 enemy prefabs

    [Header("Lighting Prefabs")]
    [SerializeField] private GameObject globalLightPrefab;
    [SerializeField] private GameObject lampPrefab;        // warm torch lamp

    [Header("Camera")]
    [SerializeField] private float baseCameraSize = 5f;

    [Header("UI")]
    [SerializeField] private Canvas cutsceneCanvas;
    [SerializeField] private Image  fadeImage;              // full-screen black Image for fades
    [SerializeField] private Text   chronicleText;          // or use TextMeshProUGUI
    [SerializeField] private Text   titleText;
    [SerializeField] private Text   skipText;

    [Header("Narrative")]
    [SerializeField] [TextArea(3, 6)]
    private string chronicleMessage =
        "Another soul stumbles into the dark.\nThe Grimoire stirs... it has been waiting.\nLet us see what you are made of.";
    [SerializeField] private string titleMessage = "EVERCHANGING GRIMOIRE";

    [Header("Scene Transition")]
    [SerializeField] private string gameplaySceneName = "StageTest";

    // ── Runtime References ───────────────────────────────────────────────────
    private Camera           mainCam;
    private CinemachineBrain cinemachineBrain;
    private GameObject       playerObj;
    private Animator         playerAnimator;
    private List<GameObject> spawnedEnemies = new List<GameObject>();
    private Light2D          globalLight;
    private List<Light2D>    sceneLights = new List<Light2D>();
    private bool             skipping;

    // ── Lifecycle ────────────────────────────────────────────────────────────
    private void Start()
    {
        SetupCamera();
        SetupLighting();
        SetupUI();
        StartCoroutine(RunCutscene());
    }

    private void Update()
    {
        if (!skipping && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape)
                          || Input.GetMouseButtonDown(0)))
        {
            skipping = true;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  SETUP
    // ═════════════════════════════════════════════════════════════════════════

    private void SetupCamera()
    {
        // Create camera from scratch so we don't depend on a prefab
        var camObj = new GameObject("CutsceneCamera");
        mainCam = camObj.AddComponent<Camera>();
        mainCam.orthographic = true;
        mainCam.orthographicSize = baseCameraSize;
        mainCam.backgroundColor = Color.black;
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.transform.position = new Vector3(0f, 0f, -10f);

        // Tag as main camera
        camObj.tag = "MainCamera";

        // Add audio listener
        camObj.AddComponent<AudioListener>();

        // Cinemachine brain for smooth camera work
        cinemachineBrain = camObj.AddComponent<CinemachineBrain>();
        cinemachineBrain.m_DefaultBlend = new CinemachineBlendDefinition(
            CinemachineBlendDefinition.Style.EaseInOut, 1.5f);
    }

    private void SetupLighting()
    {
        // Start in near-total darkness
        if (globalLightPrefab != null)
        {
            var go = Instantiate(globalLightPrefab);
            globalLight = go.GetComponent<Light2D>();
        }
        else
        {
            var go = new GameObject("GlobalLight");
            globalLight = go.AddComponent<Light2D>();
            globalLight.lightType = Light2D.LightType.Global;
        }
        globalLight.intensity = 0f; // pitch black at start
    }

    private void SetupUI()
    {
        // Create canvas if not provided
        if (cutsceneCanvas == null)
        {
            var canvasObj = new GameObject("CutsceneCanvas");
            cutsceneCanvas = canvasObj.AddComponent<Canvas>();
            cutsceneCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            cutsceneCanvas.sortingOrder = 100;
            canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.AddComponent<GraphicRaycaster>();

            var scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
        }

        // Fade overlay
        if (fadeImage == null)
        {
            var fadeObj = new GameObject("FadeOverlay");
            fadeObj.transform.SetParent(cutsceneCanvas.transform, false);
            fadeImage = fadeObj.AddComponent<Image>();
            fadeImage.color = Color.black;
            fadeImage.raycastTarget = false;
            var rt = fadeImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        fadeImage.color = Color.black; // start fully black

        // Chronicle text
        if (chronicleText == null)
        {
            chronicleText = CreateUIText("ChronicleText", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.15f), new Vector2(0.9f, 0.55f));
            chronicleText.fontStyle = FontStyle.Italic;
        }
        chronicleText.text = "";
        chronicleText.color = new Color(0.85f, 0.75f, 0.55f, 0f); // gold, invisible

        // Title text
        if (titleText == null)
        {
            titleText = CreateUIText("TitleText", 52, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.65f));
            titleText.fontStyle = FontStyle.Bold;
        }
        titleText.text = "";
        titleText.color = new Color(1f, 0.9f, 0.6f, 0f);

        // Skip prompt
        if (skipText == null)
        {
            skipText = CreateUIText("SkipText", 16, TextAnchor.LowerRight,
                new Vector2(0.75f, 0.02f), new Vector2(0.98f, 0.06f));
        }
        skipText.text = "Press any key to skip";
        skipText.color = new Color(1f, 1f, 1f, 0f);
    }

    private Text CreateUIText(string name, int fontSize, TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(cutsceneCanvas.transform, false);
        var text = obj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var rt = text.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return text;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CUTSCENE SEQUENCE
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator RunCutscene()
    {
        // Show skip hint after a beat
        yield return Wait(0.5f);
        yield return FadeTextAlpha(skipText, 0f, 0.35f, 0.5f);

        // ── ACT 1: Darkness — Title Card ────────────────────────────────────
        yield return Wait(0.3f);
        yield return FadeTextAlpha(titleText, 0f, 1f, 1.5f);
        titleText.text = titleMessage;
        yield return TypewriterReveal(titleText, titleMessage, 0.07f);
        yield return Wait(1.5f);
        yield return FadeTextAlpha(titleText, 1f, 0f, 1f);

        if (skipping) { yield return SkipToGameplay(); yield break; }

        // ── ACT 2: The Player Emerges ───────────────────────────────────────
        // Spawn player off-screen bottom
        SpawnPlayer(new Vector3(0f, -8f, 0f));

        // Create a vcam following the player
        var playerVcam = CreateVcam("PlayerCam", playerObj.transform, baseCameraSize + 1f);

        // Slow fade from black, ambient light rises
        yield return FadeOverlay(1f, 0f, 2f);
        yield return AnimateGlobalLight(0f, 0.04f, 2f); // still very dim

        // Spawn ambient torches — dim, flickering
        SpawnTorch(new Vector3(-3f, -4f, 0f), TorchLight.ColorPreset.WarmTorch, 0.3f, 3f);
        SpawnTorch(new Vector3(3f, -2f, 0f), TorchLight.ColorPreset.WarmTorch, 0.25f, 2.5f);

        // Player walks north slowly
        yield return MoveCharacter(playerObj, new Vector3(0f, -8f), new Vector3(0f, -2f), 3f, "walk_north");

        if (skipping) { yield return SkipToGameplay(); yield break; }

        // ── ACT 3: Chronicle Speaks ─────────────────────────────────────────
        // Dramatic zoom in on player
        yield return AnimateCameraSize(playerVcam, baseCameraSize + 1f, 3.5f, 1.5f);

        // Spotlight on player — warm point light blooms
        var playerSpotlight = SpawnPointLight(playerObj.transform.position + Vector3.up * 0.3f,
            new Color(1f, 0.85f, 0.5f), 1.2f, 4f);
        yield return AnimateLightIntensity(playerSpotlight, 0f, 1.2f, 1f);

        // Player stops, faces south (toward camera)
        PlayAnimation(playerObj, "idle_south");
        yield return Wait(0.5f);

        // Chronicle text types in
        yield return FadeTextAlpha(chronicleText, 0f, 1f, 0.5f);
        yield return TypewriterReveal(chronicleText, chronicleMessage, 0.04f);
        yield return Wait(2f);

        if (skipping) { yield return SkipToGameplay(); yield break; }

        // Text fades, lights dim as danger approaches
        yield return FadeTextAlpha(chronicleText, 1f, 0f, 0.8f);
        yield return AnimateLightIntensity(playerSpotlight, 1.2f, 0.4f, 0.8f);

        // ── ACT 4: Enemies Appear ───────────────────────────────────────────
        // Camera pulls back to reveal the room
        yield return AnimateCameraSize(playerVcam, 3.5f, baseCameraSize + 2f, 2f);

        // Ambient light shifts to ominous
        yield return AnimateGlobalLight(0.04f, 0.08f, 1f);

        // Spawn enemies at edges with dramatic red lights
        Vector3[] enemyPositions = {
            new Vector3(-6f, 3f),
            new Vector3(6f, 2f),
            new Vector3(-4f, 5f),
            new Vector3(5f, 5f),
        };

        for (int i = 0; i < Mathf.Min(enemyPrefabs.Length, enemyPositions.Length); i++)
        {
            var enemy = SpawnEnemy(enemyPrefabs[i], enemyPositions[i]);
            spawnedEnemies.Add(enemy);

            // Dramatic red light per enemy
            var enemyLight = SpawnPointLight(enemyPositions[i],
                new Color(0.9f, 0.15f, 0.1f), 0f, 3f);
            yield return AnimateLightIntensity(enemyLight, 0f, 0.8f, 0.3f);

            // Stagger enemy reveals
            yield return Wait(0.35f);
        }

        if (skipping) { yield return SkipToGameplay(); yield break; }

        // Enemies walk toward player menacingly
        var enemyMoveRoutines = new List<Coroutine>();
        for (int i = 0; i < spawnedEnemies.Count; i++)
        {
            Vector3 target = Vector3.Lerp(spawnedEnemies[i].transform.position,
                playerObj.transform.position, 0.4f);
            string dir = GetWalkDirection(spawnedEnemies[i].transform.position, playerObj.transform.position);
            enemyMoveRoutines.Add(StartCoroutine(
                MoveCharacter(spawnedEnemies[i], spawnedEnemies[i].transform.position, target, 2f, dir)));
        }

        // While enemies approach, zoom camera slightly
        yield return AnimateCameraSize(playerVcam, baseCameraSize + 2f, baseCameraSize + 0.5f, 2f);

        // Wait for enemy movement to finish
        foreach (var c in enemyMoveRoutines)
            yield return c;

        if (skipping) { yield return SkipToGameplay(); yield break; }

        // ── ACT 5: Standoff & Flash ─────────────────────────────────────────
        // Player turns to face the nearest enemy
        PlayAnimation(playerObj, "idle_north");
        yield return Wait(0.8f);

        // Dramatic flash — screen goes white briefly
        yield return ScreenFlash(new Color(1f, 0.95f, 0.8f), 0.15f, 0.6f);

        // All lights surge
        yield return AnimateGlobalLight(0.08f, 0.5f, 0.3f);
        yield return Wait(0.3f);

        // Camera shake
        yield return CameraShake(0.3f, 0.15f);

        // ── ACT 6: Transition to Gameplay ───────────────────────────────────
        // Quick zoom back to normal + light settles
        yield return AnimateGlobalLight(0.5f, 0.15f, 0.5f);
        yield return AnimateCameraSize(playerVcam, baseCameraSize + 0.5f, baseCameraSize, 0.8f);

        yield return Wait(0.3f);

        // Fade to black
        yield return FadeOverlay(0f, 1f, 1.5f);
        yield return Wait(0.5f);

        // Load gameplay
        SceneManager.LoadScene(gameplaySceneName);
    }

    private IEnumerator SkipToGameplay()
    {
        yield return FadeOverlay(fadeImage.color.a, 1f, 0.5f);
        SceneManager.LoadScene(gameplaySceneName);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  SPAWNING
    // ═════════════════════════════════════════════════════════════════════════

    private void SpawnPlayer(Vector3 position)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[IntroCutscene] Player prefab not assigned!");
            return;
        }

        playerObj = Instantiate(playerPrefab, position, Quaternion.identity);
        playerObj.name = "CutscenePlayer";

        // Disable gameplay components — we drive movement manually
        DisableComponent<PlayerMovement>(playerObj);
        DisableComponent<Health>(playerObj);
        DisableComponent<PlayerHitEffect>(playerObj);
        DisableComponent<PlayerStatusEffects>(playerObj);
        DisableRigidbody(playerObj);

        // Disable any existing spell systems
        foreach (var exec in playerObj.GetComponentsInChildren<SpellExecutor>())
            exec.enabled = false;

        playerAnimator = playerObj.GetComponentInChildren<Animator>();

        // Disable PlayerAnimator (we drive clips directly)
        var pa = playerObj.GetComponentInChildren<PlayerAnimator>();
        if (pa != null) pa.enabled = false;
    }

    private GameObject SpawnEnemy(GameObject prefab, Vector3 position)
    {
        var enemy = Instantiate(prefab, position, Quaternion.identity);
        enemy.name = prefab.name.Replace("Variant", "").Trim() + "_Cutscene";

        // Disable all gameplay components
        DisableComponent<EnemyBase>(enemy);
        DisableComponent<Health>(enemy);
        DisableRigidbody(enemy);
        DisableComponent<Pathfinding.Seeker>(enemy);
        DisableComponent<EnemyHealthBar>(enemy);
        DisableComponent<EnemyRegen>(enemy);

        // Keep Animator alive — we need it for walk animations
        var animator = enemy.GetComponent<Animator>();
        if (animator != null) animator.enabled = true;

        return enemy;
    }

    private void DisableComponent<T>(GameObject obj) where T : Behaviour
    {
        var comp = obj.GetComponentInChildren<T>();
        if (comp != null) comp.enabled = false;
    }

    private void DisableRigidbody(GameObject obj)
    {
        var rb = obj.GetComponentInChildren<Rigidbody2D>();
        if (rb != null) rb.simulated = false;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LIGHTING
    // ═════════════════════════════════════════════════════════════════════════

    private Light2D SpawnPointLight(Vector3 position, Color color, float intensity, float radius)
    {
        var go = new GameObject("PointLight");
        go.transform.position = position;
        var light = go.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.pointLightOuterRadius = radius;
        light.pointLightInnerRadius = radius * 0.3f;
        return light;
    }

    private void SpawnTorch(Vector3 position, TorchLight.ColorPreset preset, float intensity, float radius)
    {
        if (lampPrefab != null)
        {
            var lamp = Instantiate(lampPrefab, position, Quaternion.identity);
            var torch = lamp.GetComponent<TorchLight>();
            if (torch != null)
            {
                torch.preset = preset;
                torch.baseIntensity = intensity;
            }
            return;
        }

        // Fallback: create a point light with flicker
        var go = new GameObject("Torch");
        go.transform.position = position;
        var light = go.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.pointLightOuterRadius = radius;
        light.pointLightInnerRadius = radius * 0.2f;
        light.intensity = intensity;
        var torchComp = go.AddComponent<TorchLight>();
        torchComp.preset = preset;
        torchComp.baseIntensity = intensity;
    }

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

    private IEnumerator AnimateLightIntensity(Light2D light, float from, float to, float duration)
    {
        if (light == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (skipping) { light.intensity = to; yield break; }
            elapsed += Time.deltaTime;
            light.intensity = Mathf.SmoothStep(from, to, elapsed / duration);
            yield return null;
        }
        light.intensity = to;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CAMERA
    // ═════════════════════════════════════════════════════════════════════════

    private CinemachineVirtualCamera CreateVcam(string name, Transform follow, float size)
    {
        var go = new GameObject(name);
        var vcam = go.AddComponent<CinemachineVirtualCamera>();
        vcam.Follow = follow;
        vcam.m_Lens.OrthographicSize = size;
        vcam.m_Lens.NearClipPlane = -10f;

        // Framing transposer for smooth follow
        var body = vcam.AddCinemachineComponent<CinemachineFramingTransposer>();
        body.m_TrackedObjectOffset = Vector3.zero;
        body.m_LookaheadTime = 0f;
        body.m_XDamping = 1.5f;
        body.m_YDamping = 1.5f;
        body.m_DeadZoneWidth = 0f;
        body.m_DeadZoneHeight = 0f;

        return vcam;
    }

    private IEnumerator AnimateCameraSize(CinemachineVirtualCamera vcam, float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (skipping) { vcam.m_Lens.OrthographicSize = to; yield break; }
            elapsed += Time.deltaTime;
            float t = EaseInOutCubic(elapsed / duration);
            vcam.m_Lens.OrthographicSize = Mathf.Lerp(from, to, t);
            yield return null;
        }
        vcam.m_Lens.OrthographicSize = to;
    }

    private IEnumerator CameraShake(float duration, float magnitude)
    {
        if (mainCam == null) yield break;
        Vector3 originalPos = mainCam.transform.localPosition;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (skipping) break;
            elapsed += Time.deltaTime;
            float dampening = 1f - (elapsed / duration);
            float x = Random.Range(-1f, 1f) * magnitude * dampening;
            float y = Random.Range(-1f, 1f) * magnitude * dampening;
            mainCam.transform.localPosition = originalPos + new Vector3(x, y, 0f);
            yield return null;
        }
        mainCam.transform.localPosition = originalPos;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CHARACTER MOVEMENT & ANIMATION
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator MoveCharacter(GameObject character, Vector3 from, Vector3 to, float duration, string walkClip)
    {
        if (character == null) yield break;

        var animator = character.GetComponentInChildren<Animator>();
        if (animator != null)
            animator.Play(walkClip);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (skipping || character == null) yield break;
            elapsed += Time.deltaTime;
            float t = EaseInOutCubic(elapsed / duration);
            character.transform.position = Vector3.Lerp(from, to, t);
            yield return null;
        }
        if (character != null)
            character.transform.position = to;
    }

    private void PlayAnimation(GameObject character, string clip)
    {
        if (character == null) return;
        var animator = character.GetComponentInChildren<Animator>();
        if (animator != null)
            animator.Play(clip);
    }

    private string GetWalkDirection(Vector3 from, Vector3 to)
    {
        Vector2 dir = ((Vector2)(to - from)).normalized;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (angle < 0) angle += 360f;

        string dirKey;
        if (angle >= 337.5f || angle < 22.5f)  dirKey = "east";
        else if (angle < 67.5f)                 dirKey = "north_east";
        else if (angle < 112.5f)                dirKey = "north";
        else if (angle < 157.5f)                dirKey = "north_west";
        else if (angle < 202.5f)                dirKey = "west";
        else if (angle < 247.5f)                dirKey = "south_west";
        else if (angle < 292.5f)                dirKey = "south";
        else                                    dirKey = "south_east";

        return "walk_" + dirKey;
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

    private IEnumerator ScreenFlash(Color flashColor, float peakDuration, float fadeDuration)
    {
        if (fadeImage == null) yield break;

        // Flash to color
        fadeImage.color = flashColor;
        yield return Wait(peakDuration);

        // Fade back to transparent
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            if (skipping) { fadeImage.color = new Color(0, 0, 0, 0); yield break; }
            elapsed += Time.deltaTime;
            Color c = Color.Lerp(flashColor, new Color(flashColor.r, flashColor.g, flashColor.b, 0f),
                EaseOutCubic(elapsed / fadeDuration));
            fadeImage.color = c;
            yield return null;
        }
        fadeImage.color = new Color(0, 0, 0, 0);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  TEXT EFFECTS
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator TypewriterReveal(Text textComp, string fullText, float charDelay)
    {
        textComp.text = "";
        for (int i = 0; i < fullText.Length; i++)
        {
            if (skipping)
            {
                textComp.text = fullText;
                yield break;
            }
            textComp.text = fullText.Substring(0, i + 1);

            // Slightly longer pause on punctuation for dramatic pacing
            float delay = charDelay;
            char ch = fullText[i];
            if (ch == '.' || ch == '!' || ch == '?') delay *= 6f;
            else if (ch == ',') delay *= 3f;
            else if (ch == '\n') delay *= 4f;

            yield return new WaitForSeconds(delay);
        }
    }

    private IEnumerator FadeTextAlpha(Text textComp, float from, float to, float duration)
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
    //  PARTICLES — Ambient Dust
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Call once during setup to add atmospheric floating dust particles to the scene.
    /// </summary>
    public void SpawnAmbientDust()
    {
        var go = new GameObject("AmbientDust");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.9f, 0.7f, 0.15f),
            new Color(1f, 1f, 1f, 0.25f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        main.gravityModifier = -0.01f; // slight upward drift

        var emission = ps.emission;
        emission.rateOverTime = 15f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(18f, 12f, 0.1f);
        shape.position = Vector3.zero;

        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        vol.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.08f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.2f, 1f),
            new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        // Renderer
        var rend = ps.GetComponent<ParticleSystemRenderer>();
        var shaderNames = new[] {
            "Universal Render Pipeline/Particles/Unlit",
            "Particles/Standard Unlit",
            "Sprites/Default"
        };
        Shader shader = null;
        foreach (var s in shaderNames) { shader = Shader.Find(s); if (shader != null) break; }
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.color = Color.white;
            Sprite knob = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            if (knob != null) mat.mainTexture = knob.texture;
            rend.material = mat;
        }
        rend.sortingLayerName = "Default";
        rend.sortingOrder = 5;

        ps.Play();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UTILITY
    // ═════════════════════════════════════════════════════════════════════════

    private IEnumerator Wait(float seconds)
    {
        if (skipping) yield break;
        yield return new WaitForSeconds(seconds);
    }

    private static float EaseInOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }

    private static float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }
}
