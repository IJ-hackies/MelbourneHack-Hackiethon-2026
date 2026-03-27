using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Run via: Tools → Set All Tiles to Grid Collider
/// Sets every Tile asset in the project to Collider Type = Grid (full-cell rectangle).
/// This fixes diagonal phantom walls caused by auto-traced sprite physics shapes.
/// </summary>
public static class TileColliderTypeSetter
{
    [MenuItem("Tools/Set All Tiles to Grid Collider")]
    public static void SetAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:Tile");

        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("No Tiles Found", "No Tile assets found in the project.", "OK");
            return;
        }

        int changed = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null) continue;

            if (tile.colliderType != Tile.ColliderType.Grid)
            {
                tile.colliderType = Tile.ColliderType.Grid;
                EditorUtility.SetDirty(tile);
                changed++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[TileColliderSetter] Done — {changed}/{guids.Length} tiles set to Grid collider type.");
        EditorUtility.DisplayDialog("Done",
            $"{changed} tile(s) updated to Grid collider type.\n\nNow click Regenerate Geometry on your Walls CompositeCollider2D.",
            "OK");
    }
}
