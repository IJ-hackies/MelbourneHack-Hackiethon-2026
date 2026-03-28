using UnityEngine;

/// <summary>
/// Test harness for driving stage generation from a Floor Manifest JSON string.
/// Paste JSON into the Inspector text area, or leave blank to load the Stage 1 manifest.
///
/// Setup:
/// 1. Add this component to any scene GameObject.
/// 2. Wire FloorAssembler reference in the Inspector.
/// 3. Ensure FloorAssembler.assembleOnStart is OFF (StageLoader drives assembly).
/// 4. Paste your manifest JSON into the text area, or leave blank for Stage 1.
/// 5. Hit Play — or right-click > Load Stage (Editor Preview).
/// </summary>
public class StageLoader : MonoBehaviour
{
    [Header("References")]
    public FloorAssembler floorAssembler;

    [Header("Floor Manifest JSON")]
    [Tooltip("Paste a Floor Manifest JSON here. Leave empty to use the Stage 1 hardcoded manifest.")]
    [TextArea(10, 30)]
    public string manifestJson;

    private void Start()
    {
        LoadStage();
    }

    [ContextMenu("Load Stage (Editor Preview)")]
    public void LoadStage()
    {
        if (floorAssembler == null)
        {
            Debug.LogError("StageLoader: FloorAssembler reference is not set.");
            return;
        }

        string json = string.IsNullOrWhiteSpace(manifestJson) ? Stage1Json : manifestJson;

        var manifest = JsonUtility.FromJson<FloorManifestDTO>(json);
        if (manifest == null)
        {
            Debug.LogError("StageLoader: failed to parse manifest JSON.");
            return;
        }

        floorAssembler.LoadManifest(manifest);

        // Add the new spell to the Grimoire
        if (manifest.new_spell != null && !string.IsNullOrEmpty(manifest.new_spell.name))
            Grimoire.Instance?.AddSpell(manifest.new_spell.ToSpellData());

        // Apply spell corruptions to existing Grimoire entries
        if (manifest.corrupted_spells != null)
            foreach (var corruption in manifest.corrupted_spells)
                Grimoire.Instance?.ApplyCorruption(corruption);

        Debug.Log($"StageLoader: loaded floor \"{manifest.floor_name}\"");
    }

    // ---------------------------------------------------------------------------
    // Stage 1 — hardcoded, never Gemini-generated.
    // Uses dungeon tileset. Wire up dungeon chamber prefabs + melee_charger /
    // ranged_sentinel enemy prefabs in the Inspector before testing enemy spawns.
    // ---------------------------------------------------------------------------
    private const string Stage1Json = @"{
  ""floor_name"": ""The Hollow Entry"",
  ""tileset_id"": ""dungeon"",
  ""palette_override"": [],
  ""environmental_modifier"": """",
  ""stage_message"": ""Every dungeon has a beginning. This is yours."",
  ""chamber_grid"": [
    ""dungeon_open_arena_01"",
    ""dungeon_pillar_hall_02"",
    ""dungeon_open_arena_01"",
    ""dungeon_scattered_cover_04"",
    ""dungeon_scattered_cover_04"",
    ""dungeon_open_arena_01"",
    ""dungeon_pillar_hall_02"",
    ""dungeon_open_arena_01""
  ],
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
    ""merged_from"": []
  },
  ""corrupted_spells"": []
}";
}
