using System;

/// <summary>
/// Plain C# class matching the full Floor Manifest JSON returned by Gemini
/// (and the hardcoded Stage 1 manifest).
/// Deserialise with JsonUtility.FromJson&lt;FloorManifestDTO&gt;(json).
/// </summary>
[Serializable]
public class FloorManifestDTO
{
    public string          floor_name;
    public string          tileset_id;
    public string[]        palette_override;
    public string          environmental_modifier;
    public string          stage_message;
    public string[]        chamber_grid;
    public EnemySpawnDTO[] enemy_spawns;
    public SpellDTO        new_spell;
    public CutsceneStepDTO[] cutscene_steps;
    public string[]          lamp_ids;
    public int               heal_scroll_count;
}
