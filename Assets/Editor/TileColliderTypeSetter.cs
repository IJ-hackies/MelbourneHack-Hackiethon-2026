using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Two menu items for switching tile collider types:
///
/// 1. Tools > Set All Tiles to Sprite Collider
///    Uses each sprite's custom physics shape for collision.
///    Run AFTER "Tools > Apply Custom Physics Shapes from JSON".
///
/// 2. Tools > Set All Tiles to Grid Collider
///    Full-cell rectangles. Use as a fallback/reset.
/// </summary>
public static class TileColliderTypeSetter
{
    [MenuItem("Tools/Set All Tiles to Sprite Collider")]
    public static void SetAllSprite()
    {
        int changed = SetAllToType(Tile.ColliderType.Sprite);
        Debug.Log($"[TileColliderSetter] Done — {changed} tiles set to Sprite collider type.");
        EditorUtility.DisplayDialog("Done",
            $"{changed} tile(s) updated to Sprite collider type.\n\nCustom physics shapes will now be used.",
            "OK");
    }

    [MenuItem("Tools/Set All Tiles to Grid Collider")]
    public static void SetAllGrid()
    {
        int changed = SetAllToType(Tile.ColliderType.Grid);
        Debug.Log($"[TileColliderSetter] Done — {changed} tiles set to Grid collider type.");
        EditorUtility.DisplayDialog("Done",
            $"{changed} tile(s) updated to Grid collider type.",
            "OK");
    }

    private static int SetAllToType(Tile.ColliderType targetType)
    {
        string[] guids = AssetDatabase.FindAssets("t:Tile");

        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("No Tiles Found", "No Tile assets found in the project.", "OK");
            return 0;
        }

        int changed = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null) continue;

            if (tile.colliderType != targetType)
            {
                tile.colliderType = targetType;
                EditorUtility.SetDirty(tile);
                changed++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return changed;
    }
}
