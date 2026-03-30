using System;
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

    // HP tracking
    private float hpAtFloorStart;
    private Health playerHealth;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        lastPosition = transform.position;
        playerHealth = GetComponent<Health>();
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

        float hpRemaining = playerHealth != null ? playerHealth.Current : 0f;
        float hpLost = hpAtFloorStart - hpRemaining;

        var log = new SessionLogDTO
        {
            stage_number           = stageNumber,
            combat_style           = combatStyle,
            primary_element        = primaryElement,
            most_damage_taken_from = mostDamageFrom,
            hp_remaining           = Mathf.Round(hpRemaining * 10f) / 10f,
            hp_lost                = Mathf.Round(Mathf.Max(0f, hpLost) * 10f) / 10f,
            time_spent_seconds     = Mathf.Round(totalFloorTime * 10f) / 10f,
            equipped_spells        = BuildEquippedSpells()
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
        hpAtFloorStart = playerHealth != null ? playerHealth.Current : 100f;
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

    private EquippedSpellDTO[] BuildEquippedSpells()
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null) return Array.Empty<EquippedSpellDTO>();

        var result = new List<EquippedSpellDTO>();
        foreach (var spell in grimoire.AllSpells)
        {
            if (spell == null) continue;

            var tagNames = new List<string>();
            if (spell.tags != null)
                foreach (var t in spell.tags)
                    tagNames.Add(t.ToString());

            result.Add(new EquippedSpellDTO
            {
                name     = spell.spellName,
                tags     = tagNames.ToArray(),
                damage   = spell.damage,
                speed    = spell.speed,
                cooldown = spell.cooldown,
                element  = spell.element ?? "",
                is_merged = spell.isMerged
            });
        }
        return result.ToArray();
    }
}

/// <summary>
/// Plain serializable class for the session log JSON sent to Gemini.
/// </summary>
[Serializable]
public class SessionLogDTO
{
    public int      stage_number;
    public string   combat_style;
    public string   primary_element;
    public string   most_damage_taken_from;
    public float    hp_remaining;
    public float    hp_lost;
    public float    time_spent_seconds;
    public EquippedSpellDTO[] equipped_spells;
}

/// <summary>
/// Serializable spell summary included in the session log so Gemini knows
/// the full details of the player's current spell library.
/// </summary>
[Serializable]
public class EquippedSpellDTO
{
    public string   name;
    public string[] tags;
    public float    damage;
    public float    speed;
    public float    cooldown;
    public string   element;
    public bool     is_merged;
}
