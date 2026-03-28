using System;

/// <summary>
/// Matches one entry in the "enemy_spawns" array of Gemini's Floor Manifest JSON.
/// e.g. { "enemy_id": "melee_charger", "count": 3, "modifiers": ["armored"] }
/// </summary>
[Serializable]
public class EnemySpawnDTO
{
    public string   enemy_id;
    public int      count;
    public string[] modifiers;
}
