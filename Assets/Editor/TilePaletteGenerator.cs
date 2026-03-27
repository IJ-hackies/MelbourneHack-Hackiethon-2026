using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Run via: Tools → Generate All Tile Palettes
///
/// For every PNG in Assets/Art/Spritesheets/Tilesets/ (skipping Dungeon which already exists):
///   1. Creates a Tile .asset for each sliced sprite
///   2. Creates a palette .prefab with those tiles laid out in a grid
///
/// Requires "Tools → Setup All Tilesets" to have been run first so sprites are sliced.
/// </summary>
public static class TilePaletteGenerator
{
    private const string TilesetFolder  = "Assets/Art/Spritesheets/Tilesets";
    private const string PaletteBase    = "Assets/Art/Spritesheets/Tilesets/Palettes";
    private const int    TileSize       = 32;

    [MenuItem("Tools/Generate All Tile Palettes")]
    public static void GenerateAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TilesetFolder });
        int created = 0, skipped = 0;

        foreach (string guid in guids)
        {
            string texPath = AssetDatabase.GUIDToAssetPath(guid);
            if (texPath.Contains("/Palettes/")) continue;

            string lowerName  = Path.GetFileNameWithoutExtension(texPath);
            string palName    = char.ToUpper(lowerName[0]) + lowerName.Substring(1);
            string folder     = $"{PaletteBase}/{palName}";
            string prefabPath = $"{folder}/{palName}.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                Debug.Log($"[PaletteGen] Skipping {palName} — already exists.");
                skipped++;
                continue;
            }

            if (CreatePalette(texPath, palName, folder, prefabPath))
                created++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg = $"Created {created} palette(s). Skipped {skipped} (already exist).";
        Debug.Log($"[PaletteGen] Done — {msg}");
        EditorUtility.DisplayDialog("Tile Palette Generator", msg, "OK");
    }

    // -------------------------------------------------------------------------

    static bool CreatePalette(string texPath, string palName, string folder, string prefabPath)
    {
        // 1. Ensure output folder exists
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(PaletteBase, palName);

        // 2. Load sprites (must be sliced already)
        List<Sprite> sprites = GetSortedSprites(texPath);
        if (sprites.Count == 0)
        {
            Debug.LogWarning($"[PaletteGen] {palName}: no sprites found — run 'Tools → Setup All Tilesets' first.");
            return false;
        }

        // 3. Get grid layout from texture dimensions
        var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
        importer.GetSourceTextureWidthAndHeight(out int texW, out int texH);
        int cols = Mathf.Max(1, texW / TileSize);
        int rows = Mathf.Max(1, texH / TileSize);

        // 4. Create Tile assets
        var tiles = new Tile[sprites.Count];
        for (int i = 0; i < sprites.Count; i++)
        {
            string tilePath = $"{folder}/{sprites[i].name}.asset";

            // Don't overwrite existing tile assets
            tiles[i] = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if (tiles[i] == null)
            {
                tiles[i] = ScriptableObject.CreateInstance<Tile>();
                tiles[i].sprite = sprites[i];
                tiles[i].name   = sprites[i].name;
                AssetDatabase.CreateAsset(tiles[i], tilePath);
            }
        }

        // Force-import tile assets before building the prefab
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 5. Build palette GameObject in memory
        var root    = new GameObject(palName);
        var grid    = root.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);

        var layerGO = new GameObject("Layer1");
        layerGO.transform.SetParent(root.transform);
        var tilemap = layerGO.AddComponent<Tilemap>();
        layerGO.AddComponent<TilemapRenderer>();

        // 6. Place tiles in grid (rows go downward: y=0,-1,-2,...)
        int idx = 0;
        for (int row = 0; row < rows && idx < tiles.Length; row++)
            for (int col = 0; col < cols && idx < tiles.Length; col++, idx++)
                tilemap.SetTile(new Vector3Int(col, -row, 0), tiles[idx]);

        // 7. Save as prefab and clean up
        bool ok = PrefabUtility.SaveAsPrefabAsset(root, prefabPath) != null;
        Object.DestroyImmediate(root);

        if (ok)
            Debug.Log($"[PaletteGen] {palName}: {cols}×{rows} grid → {tiles.Length} tiles created.");
        else
            Debug.LogError($"[PaletteGen] {palName}: failed to save prefab at {prefabPath}");

        return ok;
    }

    // -------------------------------------------------------------------------

    static List<Sprite> GetSortedSprites(string texPath)
    {
        var sprites = new List<Sprite>();
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(texPath))
            if (obj is Sprite s) sprites.Add(s);

        sprites.Sort((a, b) => GetSuffix(a.name).CompareTo(GetSuffix(b.name)));
        return sprites;
    }

    static int GetSuffix(string name)
    {
        int u = name.LastIndexOf('_');
        return u >= 0 && int.TryParse(name.Substring(u + 1), out int i) ? i : 0;
    }
}
