using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Automatically assigns Unity physics layers to Tilemap GameObjects by name:
///   "Ground"  →  "ground" layer
///   "Walls"   →  "walls"  layer
///
/// Runs automatically on scene open and whenever the hierarchy changes.
/// Can also be triggered manually via: Tools → Apply Tilemap Layers
/// </summary>
[InitializeOnLoad]
public static class TilemapLayerAutoAssign
{
    static TilemapLayerAutoAssign()
    {
        EditorSceneManager.sceneOpened      += (scene, _) => ApplyLayers();
        EditorApplication.hierarchyChanged  += ApplyLayers;
    }

    [MenuItem("Tools/Apply Tilemap Layers")]
    public static void ApplyLayers()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        int wallsLayer  = LayerMask.NameToLayer("Walls");

        if (groundLayer == -1)
            Debug.LogWarning("[TilemapLayerAutoAssign] Layer 'Ground' not found — add it in Project Settings > Tags and Layers.");
        if (wallsLayer == -1)
            Debug.LogWarning("[TilemapLayerAutoAssign] Layer 'Walls' not found — add it in Project Settings > Tags and Layers.");

        int changed = 0;
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            int targetLayer = -1;
            if      (go.name == "Ground" && groundLayer != -1) targetLayer = groundLayer;
            else if (go.name == "Walls"  && wallsLayer  != -1) targetLayer = wallsLayer;

            if (targetLayer != -1 && go.layer != targetLayer)
            {
                Undo.RecordObject(go, "Auto-assign tilemap layer");
                go.layer = targetLayer;
                EditorUtility.SetDirty(go);
                changed++;
            }
        }

        if (changed > 0)
            Debug.Log($"[TilemapLayerAutoAssign] Assigned layers on {changed} object(s).");
    }
}
