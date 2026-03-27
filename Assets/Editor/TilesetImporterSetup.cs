using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Run via: Tools → Setup All Tilesets
/// Configures every PNG in Assets/Art/Spritesheets/Tilesets/ with:
///   - Sprite (2D and UI), Multiple mode
///   - 32 px per unit, Point filter, no compression
///   - Grid-sliced at 32×32
/// </summary>
public static class TilesetImporterSetup
{
    private const string TilesetFolder = "Assets/Art/Spritesheets/Tilesets";
    private const int TileSize = 32;

    [MenuItem("Tools/Setup All Tilesets")]
    public static void SetupAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TilesetFolder });

        if (guids.Length == 0)
        {
            Debug.LogWarning($"[TilesetSetup] No textures found in {TilesetFolder}");
            return;
        }

        int processed = 0;
        AssetDatabase.StartAssetEditing();

        try
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (ProcessTileset(path))
                    processed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[TilesetSetup] Done — {processed}/{guids.Length} tilesets configured.");
        EditorUtility.DisplayDialog("Tileset Setup", $"Done!\n{processed} tilesets sliced at {TileSize}×{TileSize}.", "OK");
    }

    private static bool ProcessTileset(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return false;

        // --- Import settings ---
        importer.textureType            = TextureImporterType.Sprite;
        importer.spriteImportMode       = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit    = TileSize;
        importer.filterMode             = FilterMode.Point;
        importer.textureCompression     = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency    = true;
        importer.mipmapEnabled          = false;
        importer.wrapMode               = TextureWrapMode.Clamp;

        // --- Get source dimensions (no need to load the texture) ---
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);

        int cols = Mathf.Max(1, width  / TileSize);
        int rows = Mathf.Max(1, height / TileSize);
        string baseName = Path.GetFileNameWithoutExtension(path);

        // --- Build sprite rects (Unity origin: bottom-left) ---
        var sprites = new List<SpriteMetaData>(cols * rows);
        int index = 0;

        for (int row = rows - 1; row >= 0; row--)
        {
            for (int col = 0; col < cols; col++)
            {
                sprites.Add(new SpriteMetaData
                {
                    name      = $"{baseName}_{index++}",
                    rect      = new Rect(col * TileSize, row * TileSize, TileSize, TileSize),
                    alignment = 0,
                    pivot     = new Vector2(0.5f, 0.5f),
                });
            }
        }

        // Use the Unity 6 data provider API (TextureImporter.spritesheet is obsolete)
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();

        // Read existing GUIDs so tile palette references survive a re-run
        var existingGUIDs = new Dictionary<string, GUID>();
        foreach (var r in dataProvider.GetSpriteRects())
            existingGUIDs[r.name] = r.spriteID;

        var spriteRects = new SpriteRect[sprites.Count];
        for (int i = 0; i < sprites.Count; i++)
        {
            spriteRects[i] = new SpriteRect
            {
                name      = sprites[i].name,
                rect      = sprites[i].rect,
                pivot     = sprites[i].pivot,
                alignment = (SpriteAlignment)sprites[i].alignment,
                spriteID  = existingGUIDs.TryGetValue(sprites[i].name, out var id) ? id : GUID.Generate(),
            };
        }

        dataProvider.SetSpriteRects(spriteRects);
        dataProvider.Apply();
        importer.SaveAndReimport();

        Debug.Log($"[TilesetSetup] {baseName}: {width}×{height}px → {cols}×{rows} grid = {sprites.Count} sprites");
        return true;
    }
}
