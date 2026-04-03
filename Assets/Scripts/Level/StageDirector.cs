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
    [SerializeField] private GameOverUI          gameOverUI;
    [SerializeField] private PlayerDeathCutscene deathCutscene;

    [Header("Spell Icons")]
    [SerializeField] private Sprite starterSpellIcon;  // StarterSpell.png — drag in Inspector

    [Header("Dungeon Start SFX")]
    [SerializeField] private AudioClip dungeonStartClip;
    [SerializeField, Range(0f, 1f)] private float dungeonStartVolume = 1f;

    [Header("Pre-generation")]
    [Tooltip("Start Gemini call when this fraction of enemies are dead.")]
    [SerializeField] [Range(0.3f, 0.9f)] private float pregenThreshold = 0.5f;

    // ── State ────────────────────────────────────────────────────────────────
    private int               stageNumber = 1;
    private FloorManifestDTO  nextManifest;
    private SpellData         nextSpellData;  // pre-created with icon generation already in flight
    private SpellTier         nextSpellTier = SpellTier.Basic; // rolled before Gemini call
    private bool              pregenStarted;
    private bool              pregenComplete;
    private bool              floorCleared;
    private float             playerHpBeforeStage;
    private string            previousManifestJson;  // JSON of the last Gemini-generated manifest

    private FloorClearDetector clearDetector;
    private SessionLogger      sessionLogger;
    private GeminiClient       geminiClient;
    private NanoBananaClient   nanoBananaClient;
    private HudIconBar         hudIconBar;
    private CutscenePlayer     cutscenePlayer;
    private Health             playerHealth;
    private bool               playerDead;

    public int StageNumber => stageNumber;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Play the dramatic intro transition when the dungeon scene first loads.
        // DungeonIntroTransition handles its own cleanup when done.
        gameObject.AddComponent<DungeonIntroTransition>();
    }

    private void Start()
    {
        // Auto-find Inspector references if not wired
        if (floorAssembler == null) floorAssembler = FindAnyObjectByType<FloorAssembler>();
        if (enemySpawner   == null) enemySpawner   = FindAnyObjectByType<EnemySpawner>();
        if (transitionUI   == null) transitionUI   = FindAnyObjectByType<StageTransitionUI>();
        if (mergeRitualUI  == null) mergeRitualUI  = FindAnyObjectByType<MergeRitualUI>();
        if (gameOverUI     == null) gameOverUI     = FindAnyObjectByType<GameOverUI>();
        if (deathCutscene  == null) deathCutscene  = FindAnyObjectByType<PlayerDeathCutscene>();
        if (deathCutscene  == null) deathCutscene  = gameObject.AddComponent<PlayerDeathCutscene>();

        // Auto-find singletons / scene objects
        clearDetector = FindAnyObjectByType<FloorClearDetector>();
        sessionLogger = FindAnyObjectByType<SessionLogger>();
        geminiClient     = FindAnyObjectByType<GeminiClient>();
        nanoBananaClient = FindAnyObjectByType<NanoBananaClient>();
        hudIconBar       = FindAnyObjectByType<HudIconBar>();
        cutscenePlayer   = FindAnyObjectByType<CutscenePlayer>();

        // Auto-load starter spell icon from Resources if not assigned in Inspector
        if (starterSpellIcon == null)
            starterSpellIcon = Resources.Load<Sprite>("SpellIcons/StarterSpell");

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerHealth = playerObj.GetComponent<Health>();
            if (playerHealth != null)
                playerHealth.OnDeath.AddListener(OnPlayerDeath);
        }

        if (floorAssembler == null)
            Debug.LogError("[StageDirector] FloorAssembler not found in scene!");
        if (enemySpawner == null)
            Debug.LogWarning("[StageDirector] EnemySpawner not found — enemies won't spawn.");

        if (clearDetector != null)
        {
            clearDetector.OnEnemyCountChanged += OnEnemyCountChanged;
            clearDetector.OnFloorCleared      += OnFloorCleared;
        }

        // Reset player to spawn position and full health for a fresh run
        ResetPlayerPosition();
        if (playerHealth != null)
            playerHealth.SetMaxHealth(200f, rescaleCurrent: false);

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
        if (playerHealth != null)
            playerHealth.OnDeath.RemoveListener(OnPlayerDeath);
    }

    // ── Player death ─────────────────────────────────────────────────────────

    private void OnPlayerDeath()
    {
        if (playerDead) return; // guard against double-fire
        playerDead = true;

        // Build context strings for Gemini narration
        string sessionLog = sessionLogger != null
            ? sessionLogger.BuildSessionLog(stageNumber)
            : "";

        string spellList = "";
        var spells = Grimoire.Instance?.AllSpells;
        if (spells != null)
            foreach (var s in spells)
                spellList += s.spellName + ", ";
        spellList = spellList.TrimEnd(',', ' ');

        // Record score — snapshots previous Furthest Page before saving
        int pagesCleared = stageNumber - 1;
        PageTracker.RecordRun(pagesCleared);

        // Stop heartbeat immediately and play death sound
        SFXManager.Instance?.StopHeartbeatImmediate();
        SFXManager.Instance?.PlayPlayerDeath();

        // Fire Gemini immediately in the background (before cutscene plays)
        gameOverUI?.PreloadNarration(stageNumber, sessionLog, spellList,
                                     pagesCleared, PageTracker.PreviousFurthestPage);

        // Play death cutscene then show Game Over UI
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (deathCutscene != null && playerObj != null)
            deathCutscene.Play(playerObj, () =>
            {
                Destroy(playerObj);
                gameOverUI?.Show();
            });
        else
        {
            if (playerObj != null) Destroy(playerObj);
            gameOverUI?.Show();
        }
    }

    // ── Stage loading ────────────────────────────────────────────────────────

    private void LoadStage(FloorManifestDTO manifest)
    {
        Debug.Log($"[StageDirector] LoadStage({stageNumber}): " +
                  $"floor_name=\"{manifest.floor_name}\", " +
                  $"tileset_id=\"{manifest.tileset_id}\", " +
                  $"enemy_spawns={manifest.enemy_spawns?.Length ?? 0}, " +
                  $"new_spell=\"{manifest.new_spell?.name}\", " +
                  $"cutscene_steps={manifest.cutscene_steps?.Length ?? 0}, " +
                  $"chamber_grid={manifest.chamber_grid?.Length ?? 0}");

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

        // Play dungeon start SFX on every floor except stage 1 (stage 1 comes from IntroCutscene)
        if (stageNumber > 1)
            SFXManager.Instance?.PlayUI(dungeonStartClip, dungeonStartVolume);

        // Load floor via FloorAssembler
        floorAssembler.LoadManifest(manifest);

        // Add new spell to Grimoire
        if (manifest.new_spell != null && !string.IsNullOrEmpty(manifest.new_spell.name))
        {
            // Use the pre-created SpellData (icon generation already in flight) if available.
            // Fall back to creating fresh if pre-generation didn't run (e.g. Stage 1, fallback manifest).
            SpellData newSpell = nextSpellData ?? manifest.new_spell.ToSpellData();
            nextSpellData = null; // consume — don't reuse on next LoadStage

            if (stageNumber == 1 && starterSpellIcon != null)
            {
                newSpell.icon = starterSpellIcon;
            }
            else if (newSpell.icon == null)
            {
                // Fallback: generate procedurally if pre-generation didn't run
                newSpell.icon = ProceduralSpellIconGenerator.Generate(newSpell);
            }

            // Ultimate spells go to the gauge-based ability AND the library (for display in Grimoire UI)
            if (newSpell.tier == SpellTier.Ultimate)
            {
                UltimateAbility.Instance?.SetSpell(newSpell);
                Grimoire.Instance?.AddSpell(newSpell); // library only; AutoEquip skips Ultimates
            }
            else
                Grimoire.Instance?.AddSpell(newSpell);
        }

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
        if (geminiClient == null)
        {
            Debug.LogWarning("[StageDirector] No GeminiClient — will use fallback manifest.");
            pregenComplete = true;
            nextManifest   = BuildFallbackManifest(stageNumber + 1);
            return;
        }

        int nextStage     = stageNumber + 1;
        nextSpellTier     = RollNextSpellTier(nextStage);
        string sessionLog = sessionLogger != null
            ? sessionLogger.BuildSessionLog(stageNumber)
            : $"{{\"stage_number\":{stageNumber}}}";

        Debug.Log($"[StageDirector] Pre-generating stage {nextStage} (kill progress: {clearDetector.KillProgress:P0}, tier={nextSpellTier})...");

        geminiClient.GenerateFloor(sessionLog, nextStage, previousManifestJson, nextSpellTier, manifest =>
        {
            if (manifest != null)
            {
                nextManifest = manifest;
                // Store this manifest as context for the next Gemini call
                previousManifestJson = JsonUtility.ToJson(manifest);
                Debug.Log($"[StageDirector] Pre-generation complete: \"{manifest.floor_name}\"");

                // Pre-create the SpellData and generate its icon immediately.
                if (manifest.new_spell != null && !string.IsNullOrEmpty(manifest.new_spell.name))
                {
                    nextSpellData = manifest.new_spell.ToSpellData();
                    nextSpellData.icon = ProceduralSpellIconGenerator.Generate(nextSpellData);
                    Grimoire.Instance?.NotifyLoadoutChanged();
                }
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

    private bool cutsceneJustPlayed;

    private void ShowTransition()
    {
        if (nextManifest == null)
        {
            Debug.LogError("[StageDirector] No manifest available for transition.");
            return;
        }

        // Play Gemini-generated cutscene first, then show the transition scroll
        if (cutscenePlayer != null
            && nextManifest.cutscene_steps != null
            && nextManifest.cutscene_steps.Length > 0)
        {
            cutsceneJustPlayed = true;
            cutscenePlayer.Play(nextManifest.cutscene_steps, () => ShowTransitionScroll());
        }
        else
        {
            cutsceneJustPlayed = false;
            ShowTransitionScroll();
        }
    }

    private void ShowTransitionScroll()
    {
        // Full health restore + 5% max HP increase each stage
        float hpIncrease = 0.05f;
        float newHp = (playerHealth != null ? playerHealth.Max : playerHpBeforeStage) * (1f + hpIncrease);

        if (transitionUI != null)
        {
            transitionUI.SetPendingSpellData(nextSpellData);

            if (cutsceneJustPlayed)
            {
                // Cutscene already faded to black and paused — skip the redundant fade,
                // go straight to the scroll (Show handles pause + "STAGE CLEARED" display).
                transitionUI.Show(nextManifest, playerHpBeforeStage, newHp, () =>
                {
                    OnTransitionComplete(newHp);
                });
            }
            else
            {
                // No cutscene — do the normal fade-to-black → "STAGE CLEARED" → scroll
                transitionUI.FadeToBlackThenShow(nextManifest, playerHpBeforeStage, newHp, () =>
                {
                    OnTransitionComplete(newHp);
                });
            }
        }
        else
        {
            OnTransitionComplete(newHp);
        }
    }

    private void OnTransitionComplete(float newHp)
    {
        AdvanceToNextStage(newHp);
    }

    private void AdvanceToNextStage(float newHp)
    {
        stageNumber++;

        // Safety: force-unpause in case any UI left timeScale at 0
        PauseManager.Reset();

        Debug.Log($"[StageDirector] AdvanceToNextStage({stageNumber}): Time.timeScale={Time.timeScale}");

        // Increase max HP and restore to full
        if (playerHealth != null)
        {
            playerHealth.SetMaxHealth(newHp, rescaleCurrent: true); // rescaleCurrent:true → keep HP ratio, no full heal
        }

        // Destroy all remaining enemies/projectiles from previous floor
        CleanupPreviousFloor();

        // Reset player position to map center
        ResetPlayerPosition();

        // Load the pre-generated manifest
        LoadStage(nextManifest);

        // Diagnose player state after stage load
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            var pm = playerObj.GetComponent<PlayerMovement>();
            var rb = playerObj.GetComponent<Rigidbody2D>();
            Debug.Log($"[StageDirector] Post-load player state: " +
                      $"pos={playerObj.transform.position}, " +
                      $"timeScale={Time.timeScale}, " +
                      $"PlayerMovement.enabled={pm?.enabled}, " +
                      $"speedMultiplier={pm?.SpeedMultiplier}, " +
                      $"rb.bodyType={rb?.bodyType}, " +
                      $"rb.simulated={rb?.simulated}, " +
                      $"rb.constraints={rb?.constraints}");
        }
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

        // Opening between chamber[0] and chamber[4] (bottom-left pair) = (10, 20).
        // Every chamber has a 4-tile gap centered on each side, so this is always clear.
        Vector3 origin = floorAssembler != null ? floorAssembler.transform.position : Vector3.zero;
        Vector3 spawnPos = origin + new Vector3(10f, 20f, 0f);
        playerObj.transform.position = spawnPos;

        var rb = playerObj.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.position = spawnPos;
            rb.linearVelocity = Vector2.zero;
        }
    }

    // ── Tier rolling ─────────────────────────────────────────────────────────

    private static SpellTier RollNextSpellTier(int stage)
    {
        // Stage 1 is always hardcoded (no Gemini call).
        // From stage 2 onward: deterministic cycle Basic→Skill→Basic→Skill→Ultimate, repeat.
        if (stage <= 1) return SpellTier.Basic;
        SpellTier[] cycle = { SpellTier.Basic, SpellTier.Skill, SpellTier.Basic, SpellTier.Skill, SpellTier.Ultimate };
        return cycle[(stage - 2) % cycle.Length];
    }

    // ── Fallback manifest (no Gemini) ────────────────────────────────────────

    private static FloorManifestDTO BuildFallbackManifest(int stage)
    {
        string[] tilesets = { "dungeon", "forest", "frozen", "flames", "acid",
                              "ocean", "rocky", "techy", "bubblegum", "honey" };
        string tileset = tilesets[(stage - 1) % tilesets.Length];

        int enemyCount = Mathf.Min(Mathf.RoundToInt(2f + stage * 1.1f), 50);

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
                        ""tier"": ""basic"",
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
                    }
                }");
            }
            return _stage1;
        }
    }
}
