using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Populate Enemy Library
/// Scans Assets/Prefabs/Enemies/ for all enemy prefabs and wires them into
/// EnemySpawner.enemyLibrary. Each prefab gets two entries:
///   1. The Gemini/manifest ID from GeminiIdMap (e.g. "melee_charger")
///   2. The snake_case prefab name as a fallback (e.g. "skeleton")
///
/// To reassign which prefab a Gemini ID points to, update GeminiIdMap below.
/// Run via Tools > Populate Enemy Library whenever you add a new enemy prefab.
/// </summary>
public static class EnemyLibraryPopulator
{
    private const string EnemyPrefabFolder = "Assets/Prefabs/Enemies";

    // Maps Gemini manifest enemy_id → prefab name (without "Variant" / "Clone").
    // These IDs must match exactly what the Floor Manifest JSON sends.
    // Add or reassign entries here as you add more enemies or tune the Gemini prompt.
    private static readonly Dictionary<string, string> GeminiIdMap = new Dictionary<string, string>
    {
        { "melee_charger",   "Skeleton"     },  // MeleeChaseAI — rushes directly at player
        { "ranged_sentinel", "FireWizard"   },  // WizardAI     — stays at distance, fires projectile
        { "fast_skitter",    "Zombie"       },  // MeleeChaseAI — low HP swarm enemy
        { "tank_brute",      "Bear"         },  // MeleeChaseAI — slow, high HP
        { "ghost",           "Ghost"        },  // GhostAI      — phases through walls
        { "vampire",         "Vampire"      },  // VampireAI
        { "dragon_newt",     "DragonNewt"   },  // DragonNewtAI
        { "evil_paladin",    "EvilPaladin"  },  // EvilPaladinAI
        { "ice_wizard",      "IceWizard"    },  // WizardAI variant with slow
        { "alien",           "Alien"        },  // AlienAI      — stands, fires projectile
    };

    [MenuItem("Tools/Populate Enemy Library")]
    public static void Populate()
    {
        var spawner = Object.FindFirstObjectByType<EnemySpawner>();
        if (spawner == null)
        {
            EditorUtility.DisplayDialog(
                "Populate Enemy Library",
                "No EnemySpawner found in the active scene. Open the gameplay scene and try again.",
                "OK");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { EnemyPrefabFolder });
        if (guids.Length == 0)
        {
            Debug.LogWarning($"EnemyLibraryPopulator: no prefabs found in {EnemyPrefabFolder}");
            return;
        }

        // Index all valid enemy prefabs by their bare name (e.g. "Skeleton", "FireWizard")
        var prefabByBareName = new Dictionary<string, GameObject>();
        foreach (string guid in guids)
        {
            string path   = AssetDatabase.GUIDToAssetPath(guid);
            var    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<EnemyBase>() == null) continue;
            string bareName = StripSuffixes(prefab.name);
            prefabByBareName[bareName] = prefab;
        }

        // Start from any manually-set entries the user has already configured
        var result = new Dictionary<string, EnemySpawner.EnemyEntry>();
        foreach (var e in spawner.enemyLibrary)
            if (!string.IsNullOrEmpty(e.id) && e.prefab != null)
                result[e.id] = e;

        int added = 0;

        // 1. Add Gemini manifest IDs (primary — these are what manifests reference)
        foreach (var kv in GeminiIdMap)
        {
            string geminiId  = kv.Key;
            string bareName  = kv.Value;

            if (result.ContainsKey(geminiId)) continue;

            if (!prefabByBareName.TryGetValue(bareName, out GameObject prefab))
            {
                Debug.LogWarning($"EnemyLibraryPopulator: GeminiIdMap entry '{geminiId}' → '{bareName}' " +
                                 $"but no matching prefab found — skipping.");
                continue;
            }

            result[geminiId] = new EnemySpawner.EnemyEntry { id = geminiId, prefab = prefab };
            added++;
        }

        // 2. Add snake_case prefab-name entries for any prefabs not already covered
        foreach (var kv in prefabByBareName)
        {
            string snakeId = ToSnakeCase(kv.Key);
            if (result.ContainsKey(snakeId)) continue;
            result[snakeId] = new EnemySpawner.EnemyEntry { id = snakeId, prefab = kv.Value };
            added++;
        }

        spawner.enemyLibrary = new List<EnemySpawner.EnemyEntry>(result.Values);
        EditorUtility.SetDirty(spawner);

        Debug.Log($"EnemyLibraryPopulator: {added} new entries added, {result.Count} total.");
        EditorUtility.DisplayDialog(
            "Populate Enemy Library",
            $"Done — {added} new entries added.\n{result.Count} total entries in library.",
            "OK");
    }

    // "DragonNewt Variant" → "DragonNewt"
    private static string StripSuffixes(string prefabName)
    {
        return Regex.Replace(prefabName, @"\s*(Variant|Clone)\s*", "", RegexOptions.IgnoreCase).Trim();
    }

    // "DragonNewt" → "dragon_newt",  "FireWizard" → "fire_wizard"
    private static string ToSnakeCase(string name)
    {
        name = Regex.Replace(name, @"([a-z])([A-Z])", "$1_$2");
        name = Regex.Replace(name, @"[\s\-]+", "_");
        return name.ToLowerInvariant();
    }
}
