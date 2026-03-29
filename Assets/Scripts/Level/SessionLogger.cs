using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks combat statistics during a floor for the Gemini session log.
/// Attach to the Player GameObject. Call BuildSessionLog() at end of floor.
/// Call ResetFloor() at the start of each new floor.
/// </summary>
public class SessionLogger : MonoBehaviour
{
    public static SessionLogger Instance { get; private set; }

    // ── Tracking state ───────────────────────────────────────────────────────
    private float totalDistanceMoved;
    private float timeSpentNearEnemies;   // within 4 units
    private float totalFloorTime;

    private Dictionary<string, float> damageByElement  = new();
    private Dictionary<string, float> damageTakenByEnemy = new();

    private Vector2 lastPosition;
    private const float NearEnemyThreshold = 4f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        lastPosition = transform.position;
    }

    private void Update()
    {
        if (PauseManager.IsPaused) return;

        totalFloorTime += Time.deltaTime;

        // Track distance moved
        Vector2 pos = transform.position;
        totalDistanceMoved += Vector2.Distance(lastPosition, pos);
        lastPosition = pos;

        // Track time spent near enemies
        var enemies = Physics2D.OverlapCircleAll(pos, NearEnemyThreshold, LayerMask.GetMask("Enemy"));
        if (enemies.Length > 0)
            timeSpentNearEnemies += Time.deltaTime;
    }

    // ── Public recording API ─────────────────────────────────────────────────

    /// <summary>Called by ProjectileHandler / SpellExecutor when a spell deals damage.</summary>
    public void RecordDamageDealt(string element, float amount)
    {
        string key = string.IsNullOrEmpty(element) ? "neutral" : element.ToLower();
        if (!damageByElement.ContainsKey(key)) damageByElement[key] = 0f;
        damageByElement[key] += amount;
    }

    /// <summary>Called when the player takes damage. Source is the enemy archetype id or "trap".</summary>
    public void RecordDamageTaken(string source, float amount)
    {
        string key = string.IsNullOrEmpty(source) ? "unknown" : source.ToLower();
        if (!damageTakenByEnemy.ContainsKey(key)) damageTakenByEnemy[key] = 0f;
        damageTakenByEnemy[key] += amount;
    }

    // ── Session log assembly ─────────────────────────────────────────────────

    /// <summary>
    /// Builds the session log JSON string for Gemini.
    /// </summary>
    public string BuildSessionLog(int stageNumber)
    {
        string combatStyle = DeriveCombatStyle();
        string primaryElement = DerivePrimaryElement();
        string mostDamageFrom = DeriveMostDamageFrom();
        string[] spellsUsed = GetSpellsUsed();

        var log = new SessionLogDTO
        {
            stage_number          = stageNumber,
            combat_style          = combatStyle,
            primary_element       = primaryElement,
            most_damage_taken_from = mostDamageFrom,
            spells_used           = spellsUsed
        };

        return JsonUtility.ToJson(log);
    }

    public void ResetFloor()
    {
        totalDistanceMoved    = 0f;
        timeSpentNearEnemies  = 0f;
        totalFloorTime        = 0f;
        damageByElement.Clear();
        damageTakenByEnemy.Clear();
        lastPosition = transform.position;
        Grimoire.Instance?.ResetUsedSpells();
    }

    // ── Derivation logic ─────────────────────────────────────────────────────

    private string DeriveCombatStyle()
    {
        if (totalFloorTime <= 0f) return "balanced";

        float nearRatio = timeSpentNearEnemies / totalFloorTime;
        float moveRate  = totalDistanceMoved / Mathf.Max(1f, totalFloorTime);

        // High movement + close to enemies = aggressive
        // Low movement + far from enemies = passive/ranged
        if (nearRatio > 0.5f && moveRate > 2f) return "aggressive";
        if (nearRatio > 0.4f)                  return "close_range";
        if (moveRate > 3f)                     return "kiting";
        if (nearRatio < 0.2f)                  return "passive";
        return "balanced";
    }

    private string DerivePrimaryElement()
    {
        if (damageByElement.Count == 0) return "neutral";

        string best = "neutral";
        float max   = 0f;
        foreach (var kv in damageByElement)
        {
            if (kv.Value > max) { max = kv.Value; best = kv.Key; }
        }
        return best;
    }

    private string DeriveMostDamageFrom()
    {
        if (damageTakenByEnemy.Count == 0) return "none";

        string worst = "none";
        float max    = 0f;
        foreach (var kv in damageTakenByEnemy)
        {
            if (kv.Value > max) { max = kv.Value; worst = kv.Key; }
        }
        return worst;
    }

    private string[] GetSpellsUsed()
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null) return System.Array.Empty<string>();

        var names = grimoire.GetUsedSpellNames();
        var arr   = new string[names.Count];
        int i     = 0;
        foreach (var n in names) arr[i++] = n;
        return arr;
    }
}

/// <summary>
/// Plain serializable class for the session log JSON sent to Gemini.
/// </summary>
[System.Serializable]
public class SessionLogDTO
{
    public int      stage_number;
    public string   combat_style;
    public string   primary_element;
    public string   most_damage_taken_from;
    public string[] spells_used;
}
