using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Run via: Tools > Configure Wall Layer Lighting
///
/// Sets up Light2D sorting layer targeting so that:
/// - Global Light and Lamp lights target "WallLayer" (walls stay lit by ambient + lamps)
/// - Player light does NOT target "WallLayer" (player can't brighten walls on overlap)
///
/// Run once after adding the "WallLayer" sorting layer.
/// </summary>
public static class ConfigureWallLighting
{
    [MenuItem("Tools/Configure Wall Layer Lighting")]
    public static void Configure()
    {
        int wallSortingLayerId = SortingLayer.NameToID("WallLayer");
        if (wallSortingLayerId == 0 && SortingLayer.IDToName(0) == "Default")
        {
            // Check if WallLayer actually exists
            bool found = false;
            foreach (var sl in SortingLayer.layers)
                if (sl.name == "WallLayer") { found = true; wallSortingLayerId = sl.id; break; }

            if (!found)
            {
                EditorUtility.DisplayDialog("Missing Sorting Layer",
                    "Sorting layer 'WallLayer' not found.\nAdd it in Project Settings > Tags and Layers.", "OK");
                return;
            }
        }

        int modified = 0;

        // --- Global Light prefab ---
        modified += ConfigurePrefabLight("Assets/Prefabs/Lighting/Global Light 2D.prefab", wallSortingLayerId, true);

        // --- All Lamp variant prefabs ---
        string[] lampGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Lighting" });
        foreach (string guid in lampGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("Global Light")) continue; // already handled
            modified += ConfigurePrefabLight(path, wallSortingLayerId, true);
        }

        // --- Player prefab: REMOVE WallLayer ---
        modified += ConfigurePrefabLight("Assets/Prefabs/Player.prefab", wallSortingLayerId, false);

        AssetDatabase.SaveAssets();

        Debug.Log($"[ConfigureWallLighting] Done — modified {modified} Light2D components.");
        EditorUtility.DisplayDialog("Wall Lighting Configured",
            $"Modified {modified} Light2D components.\n\n" +
            "- Global + Lamp lights: target WallLayer (walls lit by ambient/lamps)\n" +
            "- Player light: does NOT target WallLayer (no overlap brightening)",
            "OK");
    }

    private static int ConfigurePrefabLight(string prefabPath, int wallLayerId, bool addLayer)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[ConfigureWallLighting] Prefab not found: {prefabPath}");
            return 0;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        var lights = root.GetComponentsInChildren<Light2D>(true);

        int count = 0;
        foreach (var light in lights)
        {
            var currentLayers = new List<int>(light.targetSortingLayers);

            if (addLayer && !currentLayers.Contains(wallLayerId))
            {
                currentLayers.Add(wallLayerId);
                light.targetSortingLayers = currentLayers.ToArray();
                count++;
            }
            else if (!addLayer && currentLayers.Contains(wallLayerId))
            {
                currentLayers.Remove(wallLayerId);
                light.targetSortingLayers = currentLayers.ToArray();
                count++;
            }
        }

        if (count > 0)
        {
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[ConfigureWallLighting] {prefabPath}: {(addLayer ? "added" : "removed")} WallLayer on {count} Light2D(s).");
        }

        PrefabUtility.UnloadPrefabContents(root);
        return count;
    }
}
