using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Run via: Tools → Fix Tileset Physics Shapes
///
/// For every sliced tile in Assets/Art/Spritesheets/Tilesets/, replaces the
/// auto-generated alpha-traced physics shape with a clean full-tile rectangle.
/// This prevents the CompositeCollider2D from producing diagonal phantom walls.
///
/// Run AFTER "Tools → Setup All Tilesets" (sprites must already be sliced).
/// </summary>
public static class TilePhysicsShapeSetup
{
    private const string TilesetFolder = "Assets/Art/Spritesheets/Tilesets";
    private const int TileSize = 32;

    [MenuItem("Tools/Fix Tileset Physics Shapes")]
    public static void FixAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TilesetFolder });

        if (guids.Length == 0)
        {
            Debug.LogWarning("[TilePhysicsSetup] No textures found — run 'Tools → Setup All Tilesets' first.");
            EditorUtility.DisplayDialog("No Tilesets Found",
                "No textures found in:\n" + TilesetFolder +
                "\n\nRun 'Tools → Setup All Tilesets' first.", "OK");
            return;
        }

        int processed = 0;
        int totalSprites = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            int count = FixPhysicsShapes(path);
            if (count > 0)
            {
                processed++;
                totalSprites += count;
            }
        }

        AssetDatabase.Refresh();

        Debug.Log($"[TilePhysicsSetup] Done — {processed}/{guids.Length} tilesets, {totalSprites} sprites set to full-rect.");
        EditorUtility.DisplayDialog("Physics Shapes Fixed",
            $"{processed} tilesets processed\n{totalSprites} sprites → full rectangle physics shapes\n\nClick Regenerate on your CompositeCollider2D to apply.",
            "OK");
    }

    private static int FixPhysicsShapes(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return 0;

        if (importer.spriteImportMode != SpriteImportMode.Multiple)
        {
            Debug.LogWarning($"[TilePhysicsSetup] {Path.GetFileNameWithoutExtension(path)}: not sliced yet — skipping.");
            return 0;
        }

        var factory = new SpriteDataProviderFactories();
        factory.Init();

        var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();

        var physicsProvider = dataProvider.GetDataProvider<ISpritePhysicsOutlineDataProvider>();
        SpriteRect[] spriteRects = dataProvider.GetSpriteRects();

        if (spriteRects == null || spriteRects.Length == 0)
        {
            Debug.LogWarning($"[TilePhysicsSetup] {Path.GetFileNameWithoutExtension(path)}: no sprite rects found — skipping.");
            return 0;
        }

        // Full rectangle in sprite-local pixel space, centered at the pivot (0.5, 0.5)
        float half = TileSize / 2f;
        var fullRect = new List<Vector2[]>
        {
            new Vector2[]
            {
                new Vector2(-half, -half),
                new Vector2( half, -half),
                new Vector2( half,  half),
                new Vector2(-half,  half),
            }
        };

        foreach (var spriteRect in spriteRects)
            physicsProvider.SetOutlines(spriteRect.spriteID, fullRect);

        dataProvider.Apply();
        importer.SaveAndReimport();

        string name = Path.GetFileNameWithoutExtension(path);
        Debug.Log($"[TilePhysicsSetup] {name}: {spriteRects.Length} sprites → full-rect physics shapes applied.");
        return spriteRects.Length;
    }
}
