using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Singleton orchestrator for the full game loop:
///   Menu → Stage 1 (hardcoded) → play → pre-generate next → floor cleared →
///   transition scroll → (merge ritual every 5 stages) → load next floor → repeat.
///
/// Pre-generation: fires Gemini call when ~50% of enemies are dead so the manifest
/// is ready (or nearly ready) by the time the floor is cleared. If not ready, a
/// loading indicator is shown on the transition scroll.
///
/// Setup:
///   1. Add to a scene GameObject in GameScene.
///   2. Wire FloorAssembler, EnemySpawner, StageTransitionUI, MergeRitualUI in Inspector.
///   3. GeminiClient, Grimoire, FloorClearDetector, SessionLogger, HudIconBar are found automatically.
/// </summary>
public class StageDirector : MonoBehaviour
{
    public static StageDirector Instance { get; private set; }

    [Header("References (wire in Inspector)")]
    [SerializeField] private FloorAssembler    floorAssembler;
    [SerializeField] private EnemySpawner      enemySpawner;
    [SerializeField] private StageTransitionUI transitionUI;
    [SerializeField] private MergeRitualUI     mergeRitualUI;

    [Header("Spell Icons")]
    [SerializeField] private Sprite starterSpellIcon;  // StarterSpell.png — drag in Inspector

    [Header("Pre-generation")]
    [Tooltip("Start Gemini call when this fraction of enemies are dead.")]
    [SerializeField] [Range(0.3f, 0.9f)] private float pregenThreshold = 0.5f;

    // ── State ────────────────────────────────────────────────────────────────
    private int               stageNumber = 1;
    private FloorManifestDTO  nextManifest;
    private bool              pregenStarted;
    private bool              pregenComplete;
    private bool              floorCleared;
    private float             playerHpBeforeStage;

    private FloorClearDetector clearDetector;
    private SessionLogger      sessionLogger;
    private GeminiClient       geminiClient;
    private NanoBananaClient   nanoBananaClient;
    private HudIconBar         hudIconBar;
    private Health             playerHealth;

    public int StageNumber => stageNumber;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Auto-find Inspector references if not wired
        if (floorAssembler == null) floorAssembler = FindAnyObjectByType<FloorAssembler>();
        if (enemySpawner   == null) enemySpawner   = FindAnyObjectByType<EnemySpawner>();
        if (transitionUI   == null) transitionUI   = FindAnyObjectByType<StageTransitionUI>();
        if (mergeRitualUI  == null) mergeRitualUI  = FindAnyObjectByType<MergeRitualUI>();

        // Auto-find singletons / scene objects
        clearDetector = FindAnyObjectByType<FloorClearDetector>();
        sessionLogger = FindAnyObjectByType<SessionLogger>();
        geminiClient     = FindAnyObjectByType<GeminiClient>();
        nanoBananaClient = FindAnyObjectByType<NanoBananaClient>();
        hudIconBar       = FindAnyObjectByType<HudIconBar>();

        // Auto-load starter spell icon from Resources if not assigned in Inspector
        if (starterSpellIcon == null)
            starterSpellIcon = Resources.Load<Sprite>("SpellIcons/StarterSpell");

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            playerHealth = playerObj.GetComponent<Health>();

        if (floorAssembler == null)
            Debug.LogError("[StageDirector] FloorAssembler not found in scene!");
        if (enemySpawner == null)
            Debug.LogWarning("[StageDirector] EnemySpawner not found — enemies won't spawn.");

        if (clearDetector != null)
        {
            clearDetector.OnEnemyCountChanged += OnEnemyCountChanged;
            clearDetector.OnFloorCleared      += OnFloorCleared;
        }

        // Begin the game with Stage 1
        LoadStage(Stage1Manifest);
    }

    private void OnDestroy()
    {
        if (clearDetector != null)
        {
            clearDetector.OnEnemyCountChanged -= OnEnemyCountChanged;
            clearDetector.OnFloorCleared      -= OnFloorCleared;
        }
    }

    // ── Stage loading ────────────────────────────────────────────────────────

    private void LoadStage(FloorManifestDTO manifest)
    {
        // Reset tracking for new floor
        clearDetector?.Reset();
        sessionLogger?.ResetFloor();
        pregenStarted  = false;
        pregenComplete = false;
        floorCleared   = false;
        nextManifest   = null;

        hudIconBar?.SetStage(stageNumber);
        hudIconBar?.RefreshEnemyCount();
        playerHpBeforeStage = playerHealth != null ? playerHealth.Current : 100f;

        // Load floor via FloorAssembler
        floorAssembler.LoadManifest(manifest);

        // Add new spell to Grimoire
        if (manifest.new_spell != null && !string.IsNullOrEmpty(manifest.new_spell.name))
        {
            SpellData newSpell = manifest.new_spell.ToSpellData();

            // Assign icon: starter sprite for Stage 1, Nano Banana for generated spells
            if (stageNumber == 1 && starterSpellIcon != null)
                newSpell.icon = starterSpellIcon;
            else if (nanoBananaClient != null)
                nanoBananaClient.GenerateIcon(newSpell, icon =>
                {
                    newSpell.icon = icon;
                    Grimoire.Instance?.NotifyLoadoutChanged();
                });

            Grimoire.Instance?.AddSpell(newSpell);
        }

        // Apply corruptions
        if (manifest.corrupted_spells != null)
            foreach (var corruption in manifest.corrupted_spells)
                Grimoire.Instance?.ApplyCorruption(corruption);

        Debug.Log($"[StageDirector] Stage {stageNumber} loaded: \"{manifest.floor_name}\"");
    }

    // ── Pre-generation trigger ───────────────────────────────────────────────

    private void OnEnemyCountChanged(int alive, int total)
    {
        if (pregenStarted || stageNumber < 1) return;

        float killProgress = clearDetector.KillProgress;
        if (killProgress >= pregenThreshold)
        {
            pregenStarted = true;
            StartPregeneration();
        }
    }

    private void StartPregeneration()
    {
        if (geminiClient == null || !geminiClient.HasApiKey)
        {
            Debug.LogWarning("[StageDirector] No GeminiClient or API key — will use fallback manifest.");
            pregenComplete = true;
            nextManifest   = BuildFallbackManifest(stageNumber + 1);
            return;
        }

        int nextStage     = stageNumber + 1;
        string sessionLog = sessionLogger != null
            ? sessionLogger.BuildSessionLog(stageNumber)
            : $"{{\"stage_number\":{stageNumber}}}";

        Debug.Log($"[StageDirector] Pre-generating stage {nextStage} (kill progress: {clearDetector.KillProgress:P0})...");

        geminiClient.GenerateFloor(sessionLog, nextStage, manifest =>
        {
            if (manifest != null)
            {
                nextManifest = manifest;
                Debug.Log($"[StageDirector] Pre-generation complete: \"{manifest.floor_name}\"");
            }
            else
            {
                Debug.LogWarning("[StageDirector] Gemini returned null — using fallback manifest.");
                nextManifest = BuildFallbackManifest(nextStage);
            }
            pregenComplete = true;

            // If the floor was already cleared while we were waiting, proceed now
            if (floorCleared)
                ShowTransition();
        });
    }

    // ── Floor cleared ────────────────────────────────────────────────────────

    private void OnFloorCleared()
    {
        floorCleared = true;
        Debug.Log($"[StageDirector] Stage {stageNumber} cleared!");

        // If pre-generation hasn't started yet (e.g. very few enemies), start now
        if (!pregenStarted)
        {
            pregenStarted = true;
            StartPregeneration();
        }

        // If pre-generation is already done, show transition immediately
        if (pregenComplete)
            ShowTransition();
        else
            StartCoroutine(WaitForPregenThenTransition());
    }

    private IEnumerator WaitForPregenThenTransition()
    {
        Debug.Log("[StageDirector] Waiting for pre-generation to complete...");
        // Use WaitForSecondsRealtime so this works even if Time.timeScale == 0
        while (!pregenComplete)
            yield return new WaitForSecondsRealtime(0.1f);
        ShowTransition();
    }

    // ── Transition flow ──────────────────────────────────────────────────────

    private void ShowTransition()
    {
        if (nextManifest == null)
        {
            Debug.LogError("[StageDirector] No manifest available for transition.");
            return;
        }

        // Calculate HP for the new stage
        float hpIncrease = 0.05f; // default 5% — could come from manifest later
        float newHp = playerHpBeforeStage * (1f + hpIncrease);

        if (transitionUI != null)
        {
            transitionUI.FadeToBlackThenShow(nextManifest, playerHpBeforeStage, newHp, () =>
            {
                // After player clicks "BEGIN" on the transition scroll
                OnTransitionComplete(newHp);
            });
        }
        else
        {
            // No transition UI — proceed directly
            OnTransitionComplete(newHp);
        }
    }

    private void OnTransitionComplete(float newHp)
    {
        // Check if this is a merge stage — show merge ritual before loading
        int nextStage = stageNumber + 1;
        bool isMergeStage = nextStage >= 5 && nextStage % 5 == 0;

        if (isMergeStage && mergeRitualUI != null)
        {
            // Show merge ritual; when it closes, load the next stage
            mergeRitualUI.Open();
            StartCoroutine(WaitForMergeClose(newHp));
        }
        else
        {
            AdvanceToNextStage(newHp);
        }
    }

    private IEnumerator WaitForMergeClose(float newHp)
    {
        // Wait until the merge ritual UI is closed (player finished or skipped)
        // Use WaitForSecondsRealtime because game is paused (Time.timeScale == 0)
        while (mergeRitualUI != null && mergeRitualUI.IsOpen)
            yield return new WaitForSecondsRealtime(0.1f);
        AdvanceToNextStage(newHp);
    }

    private void AdvanceToNextStage(float newHp)
    {
        stageNumber++;

        // Scale player HP
        if (playerHealth != null)
            playerHealth.SetMaxHealth(newHp);

        // Destroy all remaining enemies/projectiles from previous floor
        CleanupPreviousFloor();

        // Reset player position to map center
        ResetPlayerPosition();

        // Load the pre-generated manifest
        LoadStage(nextManifest);
    }

    private void CleanupPreviousFloor()
    {
        // Destroy lingering enemy objects
        foreach (var enemy in FindObjectsByType<EnemyBase>(FindObjectsSortMode.None))
            if (enemy != null) Destroy(enemy.gameObject);

        // Destroy lingering enemy projectiles (FireballProjectile, ShockwaveProjectile, etc.)
        foreach (var proj in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (proj == null) continue;
            var type = proj.GetType();
            if (type == typeof(FireballProjectile)    ||
                type == typeof(ShockwaveProjectile)   ||
                type == typeof(BloodBeamProjectile)   ||
                type == typeof(GhostGooProjectile)    ||
                type == typeof(GhostFreezeZone))
                Destroy(proj.gameObject);
        }

        // Destroy lingering player spell projectiles
        foreach (var handler in FindObjectsByType<ProjectileHandler>(FindObjectsSortMode.None))
            if (handler != null) Destroy(handler.gameObject);

        // Destroy lingering orbital spells
        foreach (var orbital in FindObjectsByType<OrbitalMotion>(FindObjectsSortMode.None))
            if (orbital != null) Destroy(orbital.gameObject);

        // Destroy lingering zones
        foreach (var zone in FindObjectsByType<LingeringZone>(FindObjectsSortMode.None))
            if (zone != null) Destroy(zone.gameObject);
    }

    private void ResetPlayerPosition()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null) return;

        // Center of chamber[0] (bottom-left) = (10, 10) — guaranteed open floor
        Vector3 origin = floorAssembler != null ? floorAssembler.transform.position : Vector3.zero;
        Vector3 spawnPos = origin + new Vector3(10f, 10f, 0f);
        playerObj.transform.position = spawnPos;

        var rb = playerObj.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.position = spawnPos;
            rb.linearVelocity = Vector2.zero;
        }
    }

    // ── Fallback manifest (no Gemini) ────────────────────────────────────────

    private static FloorManifestDTO BuildFallbackManifest(int stage)
    {
        string[] tilesets = { "dungeon", "forest", "frozen", "flames", "acid",
                              "ocean", "rocky", "techy", "bubblegum", "honey" };
        string tileset = tilesets[(stage - 1) % tilesets.Length];

        int enemyCount = Mathf.Min(4 + stage, 30);

        return new FloorManifestDTO
        {
            floor_name             = $"Floor {stage}",
            tileset_id             = tileset,
            palette_override       = Array.Empty<string>(),
            environmental_modifier = "",
            stage_message          = "The dungeon shifts. Something watches.",
            enemy_spawns           = new[]
            {
                new EnemySpawnDTO { enemy_id = "melee_charger",    count = Mathf.CeilToInt(enemyCount * 0.5f), modifiers = Array.Empty<string>() },
                new EnemySpawnDTO { enemy_id = "ranged_sentinel",  count = Mathf.CeilToInt(enemyCount * 0.3f), modifiers = Array.Empty<string>() },
                new EnemySpawnDTO { enemy_id = "ghost",            count = Mathf.CeilToInt(enemyCount * 0.2f), modifiers = Array.Empty<string>() },
            },
            new_spell = new SpellDTO
            {
                name     = $"Shard of Floor {stage}",
                flavor   = "A fragment of the dungeon's will.",
                tags     = new[] { "PROJECTILE", "HOMING" },
                damage   = 15f + stage * 3f,
                speed    = 7f,
                cooldown = 0.5f,
                element  = "",
                is_merged   = false,
                merged_from = Array.Empty<string>(),
                projectile_color = "#6699FF",
                secondary_color  = "#AACCFF",
                projectile_scale = 1f,
                glow_size        = 0.4f,
                trail_length     = 0.15f,
                trail_width      = 0.12f,
                burst_count      = 1
            },
            corrupted_spells = Array.Empty<CorruptionDTO>()
        };
    }

    // ── Stage 1 hardcoded manifest ───────────────────────────────────────────

    private static FloorManifestDTO _stage1;
    private static FloorManifestDTO Stage1Manifest
    {
        get
        {
            if (_stage1 == null)
            {
                _stage1 = JsonUtility.FromJson<FloorManifestDTO>(@"{
                    ""floor_name"": ""The Hollow Entry"",
                    ""tileset_id"": ""dungeon"",
                    ""palette_override"": [],
                    ""environmental_modifier"": """",
                    ""stage_message"": ""Every dungeon has a beginning. This is yours."",
                    ""enemy_spawns"": [
                        { ""enemy_id"": ""melee_charger"", ""count"": 3, ""modifiers"": [] },
                        { ""enemy_id"": ""ranged_sentinel"", ""count"": 2, ""modifiers"": [] }
                    ],
                    ""new_spell"": {
                        ""name"": ""Void Shard"",
                        ""flavor"": ""A raw sliver of nothing, flung outward with intent."",
                        ""tags"": [""PROJECTILE""],
                        ""damage"": 20.0,
                        ""speed"": 8.0,
                        ""cooldown"": 0.4,
                        ""element"": """",
                        ""is_merged"": false,
                        ""merged_from"": [],
                        ""projectile_color"": ""#9966FF"",
                        ""secondary_color"": ""#CC99FF"",
                        ""projectile_scale"": 1.0,
                        ""glow_size"": 0.45,
                        ""trail_length"": 0.2,
                        ""trail_width"": 0.15,
                        ""burst_count"": 1
                    },
                    ""corrupted_spells"": []
                }");
            }
            return _stage1;
        }
    }
}
