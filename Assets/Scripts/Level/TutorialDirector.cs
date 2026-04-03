using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tutorial scene orchestrator — drives a linear state machine that teaches
/// the player all game mechanics via dialogue, spotlights, and guided actions.
///
/// Replaces StageDirector for the Tutorial scene. Uses a hardcoded floor
/// manifest and spells. No Gemini calls.
///
/// Setup:
///   1. Add to a GameObject in the Tutorial scene.
///   2. Wire references in Inspector (or let auto-find handle them).
/// </summary>
public class TutorialDirector : MonoBehaviour
{
    [Header("References (auto-found if not wired)")]
    [SerializeField] private FloorAssembler   floorAssembler;
    [SerializeField] private EnemySpawner     enemySpawner;
    [SerializeField] private TutorialUI       tutorialUI;
    [SerializeField] private TutorialCompleteUI completeUI;
    [SerializeField] private GrimoireUI       grimoireUI;
    [SerializeField] private MergeRitualUI    mergeRitualUI;
    [SerializeField] private HudIconBar       hudIconBar;
    [SerializeField] private SpellHotbar      spellHotbar;
    [SerializeField] private MinimapController minimapController;

    [Header("Starter Spell Icon")]
    [SerializeField] private Sprite starterSpellIcon;

    // ── State machine ────────────────────────────────────────────────────────

    private enum State
    {
        Intro,
        ShowPage,
        ShowLoadout,
        ShowDash,
        ShowHealth,
        SpawnEnemies,
        ShowMinimap,
        WaitMinimapExpand,
        WaitMinimapClose,
        StartCombat,
        FirstKillPrompt,
        WaitFirstKill,
        NewSpellGranted,
        OpenGrimoire,
        WaitGrimoireOpen,
        SelectNewSpell,
        EquipToSlot,
        CloseGrimoire,
        WaitGrimoireClose,
        OpenMerge,
        WaitMergeOpen,
        SelectMergeSpells,
        PerformRitual,
        WaitMergeDone,
        MergeComplete,
        KillRemaining,
        TutorialComplete,
        Done
    }

    private State _state = State.Intro;
    private FloorClearDetector _clearDetector;
    private PlayerHUD _playerHUD;
    private Health _playerHealth;
    private bool _firstEnemyKilled;
    private int _enemiesKilledCount;
    private SpellData _starterSpell;
    private SpellData _rewardSpell;
    private bool _waitingForSpellSelect;
    private bool _waitingForEquip;
    private int _loadoutCountBeforeEquip;
    private bool _waitingForMergeSelect;
    private bool _waitingForMergePerform;
    private int _spellCountBeforeMerge;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        // Auto-find references
        if (floorAssembler   == null) floorAssembler   = FindAnyObjectByType<FloorAssembler>();
        if (enemySpawner     == null) enemySpawner     = FindAnyObjectByType<EnemySpawner>();
        if (tutorialUI       == null) tutorialUI       = FindAnyObjectByType<TutorialUI>();
        if (completeUI       == null) completeUI       = FindAnyObjectByType<TutorialCompleteUI>();
        if (grimoireUI       == null) grimoireUI       = FindAnyObjectByType<GrimoireUI>();
        if (mergeRitualUI    == null) mergeRitualUI    = FindAnyObjectByType<MergeRitualUI>();
        if (hudIconBar       == null) hudIconBar       = FindAnyObjectByType<HudIconBar>();
        if (spellHotbar      == null) spellHotbar      = FindAnyObjectByType<SpellHotbar>();
        if (minimapController == null) minimapController = FindAnyObjectByType<MinimapController>();
        _clearDetector = FindAnyObjectByType<FloorClearDetector>();
        _playerHUD     = FindAnyObjectByType<PlayerHUD>();

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            _playerHealth = playerObj.GetComponent<Health>();

        if (starterSpellIcon == null)
            starterSpellIcon = Resources.Load<Sprite>("SpellIcons/StarterSpell");

        // Ensure Grimoire is clean
        Grimoire.Instance?.ResetAll();
        UltimateAbility.Instance?.ResetGauge();

        // Reset player to fresh state (position, health, status effects)
        ResetPlayer();

        // Create starter spell and add to grimoire
        _starterSpell = CreateStarterSpell();
        Grimoire.Instance?.AddSpell(_starterSpell);

        // Create reward spell (not added yet — given after first kill)
        _rewardSpell = CreateRewardSpell();

        // Load tutorial floor
        LoadTutorialFloor();

        // Subscribe to events
        if (_clearDetector != null)
        {
            _clearDetector.OnEnemyCountChanged += OnEnemyCountChanged;
            _clearDetector.OnFloorCleared      += OnFloorCleared;
        }

        // Start tutorial sequence after a brief delay for floor assembly
        StartCoroutine(BeginTutorialDelayed());
    }

    private void OnDestroy()
    {
        if (_clearDetector != null)
        {
            _clearDetector.OnEnemyCountChanged -= OnEnemyCountChanged;
            _clearDetector.OnFloorCleared      -= OnFloorCleared;
        }
    }

    private void Update()
    {
        // Fallback polling: if event subscription missed the kill, catch it here
        if (_state == State.WaitFirstKill && !_firstEnemyKilled && _clearDetector != null)
        {
            int killed = _clearDetector.TotalEnemies - _clearDetector.AliveEnemies;
            if (killed >= 1)
            {
                _firstEnemyKilled = true;
                EnterState(State.NewSpellGranted);
            }
        }

        if (_state == State.KillRemaining && _clearDetector != null && _clearDetector.IsCleared)
        {
            EnterState(State.TutorialComplete);
        }

        // Wait for player to click the new spell in the grimoire
        if (_waitingForSpellSelect && grimoireUI != null)
        {
            var sel = grimoireUI.SelectedSpell;
            if (sel != null && sel.spellName == _rewardSpell.spellName)
            {
                _waitingForSpellSelect = false;
                _loadoutCountBeforeEquip = CountEquippedSpells();
                EnterState(State.EquipToSlot);
            }
        }

        // Wait for player to equip the spell via Place button + slot click
        if (_waitingForEquip)
        {
            // Detect any change in equipped spells (reward spell got equipped)
            int currentCount = CountEquippedSpells();
            if (currentCount > _loadoutCountBeforeEquip || IsSpellEquippedByName(_rewardSpell.spellName))
            {
                _waitingForEquip = false;
                EnterState(State.CloseGrimoire);
            }
        }

        // Wait for player to select both spells in merge UI
        if (_waitingForMergeSelect && mergeRitualUI != null)
        {
            if (mergeRitualUI.SelectedCount >= 2)
            {
                _waitingForMergeSelect = false;
                EnterState(State.PerformRitual);
            }
        }

        // Wait for player to click Perform Ritual — detect by checking for a merged spell in library
        if (_waitingForMergePerform)
        {
            var grimoire = Grimoire.Instance;
            if (grimoire != null)
            {
                bool hasMerged = false;
                foreach (var s in grimoire.AllSpells)
                    if (s.isMerged) { hasMerged = true; break; }
                if (hasMerged)
                {
                    _waitingForMergePerform = false;
                    // Go straight to WaitMergeDone which waits for cutscene + UI to finish
                    EnterState(State.WaitMergeDone);
                }
            }
        }
    }

    private int CountEquippedSpells()
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null) return 0;
        int count = 0;
        for (int i = 0; i < Grimoire.LoadoutSize; i++)
            if (grimoire.Loadout[i] != null) count++;
        return count;
    }

    private bool IsSpellEquipped(SpellData spell)
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null || spell == null) return false;
        for (int i = 0; i < Grimoire.LoadoutSize; i++)
            if (grimoire.Loadout[i] == spell) return true;
        return false;
    }

    private bool IsSpellEquippedByName(string spellName)
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null || string.IsNullOrEmpty(spellName)) return false;
        for (int i = 0; i < Grimoire.LoadoutSize; i++)
            if (grimoire.Loadout[i] != null && grimoire.Loadout[i].spellName == spellName) return true;
        return false;
    }

    private IEnumerator BeginTutorialDelayed()
    {
        // Wait 2 frames for all UI to build
        yield return null;
        yield return null;

        hudIconBar?.SetStage(1);
        PauseManager.Pause();
        EnterState(State.Intro);
    }

    // ── State machine ────────────────────────────────────────────────────────

    private void EnterState(State state)
    {
        _state = state;

        switch (state)
        {
            case State.Intro:
                tutorialUI.ShowDialogue(DialogueIntro, () => EnterState(State.ShowPage));
                break;

            case State.ShowPage:
                tutorialUI.ShowDialogueWithSpotlight(DialogueShowPage,
                    hudIconBar?.PageDisplayRT, hudIconBar?.CanvasGO,
                    () => EnterState(State.ShowLoadout));
                break;

            case State.ShowLoadout:
                tutorialUI.ShowDialogueWithSpotlight(DialogueShowLoadout,
                    spellHotbar?.GetSlotRT(0), spellHotbar?.CanvasGO,
                    () => EnterState(State.ShowDash));
                break;

            case State.ShowDash:
                tutorialUI.ShowDialogueWithSpotlight(DialogueShowDash,
                    spellHotbar?.DashRT, spellHotbar?.CanvasGO,
                    () => EnterState(State.ShowHealth));
                break;

            case State.ShowHealth:
                tutorialUI.ShowDialogueWithSpotlight(DialogueShowHealth,
                    _playerHUD?.HealthBarRT, _playerHUD?.CanvasGO,
                    () => EnterState(State.SpawnEnemies));
                break;

            case State.SpawnEnemies:
                tutorialUI.HideAll();
                PauseManager.Reset(); // force unpaused for gameplay
                SpawnTutorialEnemies();
                StartCoroutine(WaitThenState(1.5f, State.ShowMinimap));
                break;

            case State.ShowMinimap:
                PauseManager.Reset();
                PauseManager.Pause();
                tutorialUI.ShowDialogueWithSpotlight(DialogueShowMinimap,
                    minimapController?.MinimapRT, minimapController?.CanvasGO,
                    () => EnterState(State.WaitMinimapExpand));
                break;

            case State.WaitMinimapExpand:
                tutorialUI.HideDialogue();
                // Keep game paused — UI button clicks work regardless of timeScale.
                // Toggle() will add a second Pause when expanded; Unpause on close
                // only reduces the count to 1, keeping gameplay frozen until StartCombat.
                StartCoroutine(WaitForCondition(
                    () => minimapController != null && minimapController.IsExpanded,
                    () => EnterState(State.WaitMinimapClose)));
                break;

            case State.WaitMinimapClose:
                tutorialUI.HideSpotlight();
                // Minimap is expanded and paused via its own Toggle — wait for close
                StartCoroutine(WaitForCondition(
                    () => minimapController == null || !minimapController.IsExpanded,
                    () => EnterState(State.StartCombat)));
                break;

            case State.StartCombat:
                // Clean slate — minimap Toggle may have left pause in any state
                PauseManager.Reset();
                PauseManager.Pause();
                tutorialUI.ShowDialogue(DialogueStartCombat, () =>
                {
                    tutorialUI.HideAll();
                    PauseManager.Reset(); // force unpaused for combat
                    EnterState(State.WaitFirstKill);
                });
                break;

            case State.WaitFirstKill:
                // Waiting — OnEnemyCountChanged will detect the first kill
                break;

            case State.NewSpellGranted:
                PauseManager.Reset();
                PauseManager.Pause();
                // Add reward spell to grimoire
                _rewardSpell.icon = ProceduralSpellIconGenerator.Generate(_rewardSpell);
                Grimoire.Instance?.AddSpell(_rewardSpell);
                Grimoire.Instance?.NotifyLoadoutChanged();
                tutorialUI.ShowDialogue(DialogueNewSpell, () => EnterState(State.OpenGrimoire));
                break;

            case State.OpenGrimoire:
                // Lock HUD so only grimoire button works
                if (hudIconBar != null) hudIconBar.LockedTo = "grimoire";
                // Disable delete in grimoire for the whole tutorial
                if (grimoireUI != null) grimoireUI.DeleteDisabled = true;
                tutorialUI.ShowDialogueWithSpotlight(DialogueOpenGrimoire,
                    hudIconBar?.GrimoireButtonRT, hudIconBar?.CanvasGO,
                    () => EnterState(State.WaitGrimoireOpen));
                break;

            case State.WaitGrimoireOpen:
                // Dialogue done, spotlight stays — wait for player to click grimoire button
                tutorialUI.HideDialogue();
                StartCoroutine(WaitForCondition(() => grimoireUI != null && grimoireUI.IsOpen,
                    () =>
                    {
                        // Boost grimoire canvas above overlay (231) so buttons stay clickable
                        if (grimoireUI?.CanvasGO != null)
                        {
                            var cv = grimoireUI.CanvasGO.GetComponent<Canvas>();
                            if (cv != null) cv.sortingOrder = 233;
                        }
                        tutorialUI.HideSpotlight();
                        EnterState(State.SelectNewSpell);
                    }));
                break;

            case State.SelectNewSpell:
                tutorialUI.ShowDialogue(DialogueSelectSpell, () =>
                {
                    // Dialogue done — now wait for player to click the new spell
                    tutorialUI.HideDialogue();
                    _waitingForSpellSelect = true;
                });
                break;

            case State.EquipToSlot:
                tutorialUI.ShowDialogue(DialogueEquipSlot, () =>
                {
                    // Dialogue done — now wait for player to click Place and equip
                    tutorialUI.HideDialogue();
                    _waitingForEquip = true;
                });
                break;

            case State.CloseGrimoire:
                tutorialUI.ShowDialogue(DialogueCloseGrimoire, () => EnterState(State.WaitGrimoireClose));
                break;

            case State.WaitGrimoireClose:
                tutorialUI.HideDialogue();
                StartCoroutine(WaitForCondition(() => grimoireUI == null || !grimoireUI.IsOpen,
                    () =>
                    {
                        // Restore grimoire canvas order
                        if (grimoireUI?.CanvasGO != null)
                        {
                            var cv = grimoireUI.CanvasGO.GetComponent<Canvas>();
                            if (cv != null) cv.sortingOrder = 210;
                        }
                        // Unlock HUD buttons before moving to merge
                        if (hudIconBar != null) hudIconBar.LockedTo = null;
                        EnterState(State.OpenMerge);
                    }));
                break;

            case State.OpenMerge:
                // Lock HUD so only merge button works
                if (hudIconBar != null) hudIconBar.LockedTo = "merge";
                tutorialUI.ShowDialogueWithSpotlight(DialogueOpenMerge,
                    hudIconBar?.MergeButtonRT, hudIconBar?.CanvasGO,
                    () => EnterState(State.WaitMergeOpen));
                break;

            case State.WaitMergeOpen:
                tutorialUI.HideDialogue();
                StartCoroutine(WaitForCondition(() => mergeRitualUI != null && mergeRitualUI.IsOpen,
                    () =>
                    {
                        // Boost merge canvas above overlay (231) so buttons stay clickable
                        if (mergeRitualUI?.CanvasGO != null)
                        {
                            var cv = mergeRitualUI.CanvasGO.GetComponent<Canvas>();
                            if (cv != null) cv.sortingOrder = 233;
                        }
                        tutorialUI.HideSpotlight();
                        EnterState(State.SelectMergeSpells);
                    }));
                break;

            case State.SelectMergeSpells:
                tutorialUI.ShowDialogue(DialogueSelectMerge, () =>
                {
                    tutorialUI.HideDialogue();
                    _waitingForMergeSelect = true;
                });
                break;

            case State.PerformRitual:
                tutorialUI.ShowDialogue(DialoguePerformRitual, () =>
                {
                    tutorialUI.HideDialogue();
                    _waitingForMergePerform = true;
                });
                break;

            case State.WaitMergeDone:
                tutorialUI.HideDialogue();
                // Wait for merge to complete (MergeRitualUI closes after cutscene)
                StartCoroutine(WaitForCondition(
                    () => mergeRitualUI == null || !mergeRitualUI.IsOpen,
                    () =>
                    {
                        // Restore merge canvas order
                        if (mergeRitualUI?.CanvasGO != null)
                        {
                            var cv = mergeRitualUI.CanvasGO.GetComponent<Canvas>();
                            if (cv != null) cv.sortingOrder = 220;
                        }
                        EnterState(State.MergeComplete);
                    }));
                break;

            case State.MergeComplete:
                PauseManager.Reset();
                PauseManager.Pause();
                // Unlock HUD and re-enable delete
                if (hudIconBar != null) hudIconBar.LockedTo = null;
                if (grimoireUI != null) grimoireUI.DeleteDisabled = false;
                tutorialUI.ShowDialogue(DialogueMergeComplete, () =>
                {
                    tutorialUI.HideAll();
                    PauseManager.Reset(); // force unpaused for combat
                    EnterState(State.KillRemaining);
                });
                break;

            case State.KillRemaining:
                // Player kills remaining enemies — OnFloorCleared handles completion
                break;

            case State.TutorialComplete:
                PauseManager.Reset();
                PauseManager.Pause();
                tutorialUI.HideAll();
                completeUI?.Show();
                _state = State.Done;
                break;

            case State.Done:
                break;
        }
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void OnEnemyCountChanged(int alive, int total)
    {
        int killed = total - alive;
        Debug.Log($"[TutorialDirector] OnEnemyCountChanged: alive={alive}, total={total}, killed={killed}, state={_state}");

        if (_state == State.WaitFirstKill && killed >= 1 && !_firstEnemyKilled)
        {
            _firstEnemyKilled = true;
            EnterState(State.NewSpellGranted);
        }
    }

    private void OnFloorCleared()
    {
        Debug.Log($"[TutorialDirector] OnFloorCleared! state={_state}");
        if (_state == State.KillRemaining)
        {
            EnterState(State.TutorialComplete);
        }
    }

    // ── Coroutine helpers ────────────────────────────────────────────────────

    private IEnumerator WaitThenState(float realtimeDelay, State nextState)
    {
        yield return new WaitForSecondsRealtime(realtimeDelay);
        EnterState(nextState);
    }

    private IEnumerator WaitForCondition(Func<bool> condition, Action onMet)
    {
        while (!condition())
            yield return null;
        onMet();
    }

    // ── Player reset ─────────────────────────────────────────────────────────

    private void ResetPlayer()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null) return;

        // Reset position to map spawn point
        Vector3 origin = floorAssembler != null ? floorAssembler.transform.position : Vector3.zero;
        Vector3 spawnPos = origin + new Vector3(10f, 20f, 0f);
        playerObj.transform.position = spawnPos;

        var rb = playerObj.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.position = spawnPos;
            rb.linearVelocity = Vector2.zero;
        }

        // Reset health to full
        var health = playerObj.GetComponent<Health>();
        if (health != null)
        {
            health.SetMaxHealth(200f, rescaleCurrent: false);
        }

        // Re-enable movement in case it was disabled
        var movement = playerObj.GetComponent<PlayerMovement>();
        if (movement != null)
            movement.enabled = true;
    }

    // ── Floor loading ────────────────────────────────────────────────────────

    private void LoadTutorialFloor()
    {
        var manifest = TutorialFloorManifest;

        _clearDetector?.Reset();
        hudIconBar?.SetStage(1);
        hudIconBar?.RefreshEnemyCount();

        floorAssembler?.LoadManifest(manifest);
    }

    private void SpawnTutorialEnemies()
    {
        var spawns = new List<EnemySpawnDTO>
        {
            new EnemySpawnDTO { enemy_id = "zombie",   count = 3, modifiers = Array.Empty<string>() },
            new EnemySpawnDTO { enemy_id = "skeleton", count = 2, modifiers = Array.Empty<string>() },
        };

        Vector2 origin = floorAssembler != null
            ? (Vector2)floorAssembler.transform.position
            : Vector2.zero;

        enemySpawner?.SpawnFloor(spawns, origin);
    }

    // ── Hardcoded data ───────────────────────────────────────────────────────

    private static FloorManifestDTO _tutorialManifest;
    private static FloorManifestDTO TutorialFloorManifest
    {
        get
        {
            if (_tutorialManifest == null)
            {
                _tutorialManifest = JsonUtility.FromJson<FloorManifestDTO>(@"{
                    ""floor_name"": ""The First Lesson"",
                    ""tileset_id"": ""dungeon"",
                    ""palette_override"": [],
                    ""environmental_modifier"": """",
                    ""stage_message"": """",
                    ""enemy_spawns"": [],
                    ""new_spell"": null
                }");
            }
            return _tutorialManifest;
        }
    }

    private SpellData CreateStarterSpell()
    {
        var spell = ScriptableObject.CreateInstance<SpellData>();
        spell.spellName = "Void Shard";
        spell.flavor = "A raw sliver of nothing, flung outward with intent.";
        spell.tags = new[] { SpellTag.PROJECTILE };
        spell.damage = 20f;
        spell.speed = 8f;
        spell.cooldown = 0.4f;
        spell.element = "";
        spell.tier = SpellTier.Basic;
        spell.isMerged = false;
        spell.mergedFrom = Array.Empty<string>();
        spell.projectileColor = "#9966FF";
        spell.secondaryColor = "#CC99FF";
        spell.projectileScale = 1f;
        spell.glowSize = 0.45f;
        spell.trailLength = 0.2f;
        spell.trailWidth = 0.15f;
        spell.burstCount = 1;

        if (starterSpellIcon != null)
            spell.icon = starterSpellIcon;
        else
            spell.icon = ProceduralSpellIconGenerator.Generate(spell);

        return spell;
    }

    private SpellData CreateRewardSpell()
    {
        var spell = ScriptableObject.CreateInstance<SpellData>();
        spell.spellName = "Bone Splinter";
        spell.flavor = "A shard ripped from the fallen. It remembers its skeleton.";
        spell.tags = new[] { SpellTag.PROJECTILE, SpellTag.PIERCE };
        spell.damage = 18f;
        spell.speed = 7f;
        spell.cooldown = 0.5f;
        spell.element = "";
        spell.tier = SpellTier.Basic;
        spell.isMerged = false;
        spell.mergedFrom = Array.Empty<string>();
        spell.projectileColor = "#CCBB88";
        spell.secondaryColor = "#EEDDAA";
        spell.projectileScale = 1f;
        spell.glowSize = 0.35f;
        spell.trailLength = 0.18f;
        spell.trailWidth = 0.12f;
        spell.burstCount = 1;
        return spell;
    }

    // ── Dialogue lines ───────────────────────────────────────────────────────

    private static readonly string[] DialogueIntro = {
        "Oh. A new Seeker. How... quaint.",
        "The Grimoire has swallowed thousands before you. Most didn't last a single page.",
        "But fine. I'll hold your hand. Just this once."
    };

    private static readonly string[] DialogueShowPage = {
        "See that number at the top? That's the page you're on.",
        "Clear it. Or die trying. I genuinely don't care which."
    };

    private static readonly string[] DialogueShowLoadout = {
        "Those icons in the bottom left? Those are your spells.",
        "Left click for your basic attack. E and Q for skills. X for your ultimate.",
        "Each has a cooldown. Mash all you want \u2014 they won't fire faster.",
        "Your ultimate charges when you damage enemies. Hit enough of them and maybe you'll earn it."
    };

    private static readonly string[] DialogueShowDash = {
        "That little icon by your health? Press Shift to dash.",
        "It makes you faster for a moment. You'll need it. Trust me."
    };

    private static readonly string[] DialogueShowHealth = {
        "This is your health. When it reaches zero, you become part of my collection.",
        "I suggest you don't let that happen. Or do. I'm not picky."
    };

    private static readonly string[] DialogueShowMinimap = {
        "That's your minimap. Red dots are things that want to kill you.",
        "The yellow dot is you. As you explore, the fog lifts.",
        "Click on it now. Go on. Expand it."
    };

    private static readonly string[] DialogueStartCombat = {
        "Enough hand-holding. Time to see what you're made of.",
        "Your basic attack fires toward your cursor. Point. Click. Kill."
    };

    private static readonly string[] DialogueNewSpell = {
        "Well. You killed one. Don't let it go to your head.",
        "The Grimoire offers you a gift. Take it. You'll need every scrap of power."
    };

    private static readonly string[] DialogueOpenGrimoire = {
        "Open the Grimoire to see your new spell."
    };

    private static readonly string[] DialogueSelectSpell = {
        "Click on your new spell to inspect it."
    };

    private static readonly string[] DialogueEquipSlot = {
        "Now click the \"Place\" button, then click the basic attack slot to equip it."
    };

    private static readonly string[] DialogueCloseGrimoire = {
        "Close the Grimoire. We're not done yet."
    };

    private static readonly string[] DialogueOpenMerge = {
        "See that merge icon? Open it.",
        "You can fuse spells of the same tier into something... more.",
        "The Grimoire hates when you do this. Which means you should."
    };

    private static readonly string[] DialogueSelectMerge = {
        "Select both of your basic spells."
    };

    private static readonly string[] DialoguePerformRitual = {
        "Now perform the ritual. Let's see what you forge."
    };

    private static readonly string[] DialogueMergeComplete = {
        "Interesting. A merged spell. It fires both projectiles at once.",
        "It's already equipped. Now use it on the rest of them."
    };
}
