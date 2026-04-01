using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Two menu items:
///
/// 1. Tools > Fix Tileset Physics Shapes (Full Rect)
///    Sets every tile in every tileset to a full-tile rectangle. Use as a reset.
///
/// 2. Tools > Apply Custom Physics Shapes from JSON
///    Reads physics_shapes.json from the project root and applies per-tile polygon
///    shapes to ALL 10 tilesets. Tiles not listed in the JSON keep a full rectangle.
///
///    The JSON is exported from physics-shape-painter.html. Format:
///    {
///      "0": [ {"x": -16, "y": -16}, {"x": 16, "y": -16}, {"x": 16, "y": 16}, {"x": -16, "y": 16} ],
///      "5": [ {"x": -16, "y": 0}, {"x": 16, "y": 0}, {"x": 16, "y": 16}, {"x": -16, "y": 16} ]
///    }
///    Coordinates are sprite-local pixels centered at pivot: -16 to +16 for 32px tiles.
///
/// Run AFTER "Tools > Setup All Tilesets" (sprites must already be sliced).
/// </summary>
public static class TilePhysicsShapeSetup
{
    private const string TilesetFolder = "Assets/Art/Spritesheets/Tilesets";
    private const string JsonPath = "physics_shapes.json";
    private const int TileSize = 32;

    // -----------------------------------------------------------------------
    // Full-rect fallback (original behavior)
    // -----------------------------------------------------------------------

    [MenuItem("Tools/Fix Tileset Physics Shapes (Full Rect)")]
    public static void FixAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TilesetFolder });

        if (guids.Length == 0)
        {
            Debug.LogWarning("[TilePhysicsSetup] No textures found.");
            return;
        }

        int processed = 0;
        int totalSprites = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            int count = ApplyShapes(path, null);
            if (count > 0) { processed++; totalSprites += count; }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[TilePhysicsSetup] Done — {processed}/{guids.Length} tilesets, {totalSprites} sprites set to full-rect.");
        EditorUtility.DisplayDialog("Physics Shapes Fixed",
            $"{processed} tilesets processed\n{totalSprites} sprites -> full rectangle physics shapes", "OK");
    }

    // -----------------------------------------------------------------------
    // JSON-driven custom shapes
    // -----------------------------------------------------------------------

    [MenuItem("Tools/Apply Custom Physics Shapes from JSON")]
    public static void ApplyFromJson()
    {
        string fullPath = Path.Combine(Application.dataPath, "..", JsonPath);

        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[TilePhysicsSetup] {JsonPath} not found in project root. " +
                           "Export it from physics-shape-painter.html first.");
            EditorUtility.DisplayDialog("File Not Found",
                $"Could not find:\n{fullPath}\n\nExport from physics-shape-painter.html and save as physics_shapes.json in the project root.", "OK");
            return;
        }

        string json = File.ReadAllText(fullPath);
        var customShapes = ParseShapeJson(json);

        if (customShapes == null || customShapes.Count == 0)
        {
            Debug.LogError("[TilePhysicsSetup] No valid shapes found in JSON.");
            return;
        }

        Debug.Log($"[TilePhysicsSetup] Loaded {customShapes.Count} custom tile shapes from {JsonPath}");

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TilesetFolder });

        if (guids.Length == 0)
        {
            Debug.LogWarning("[TilePhysicsSetup] No textures found.");
            return;
        }

        int processed = 0;
        int totalSprites = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            int count = ApplyShapes(path, customShapes);
            if (count > 0) { processed++; totalSprites += count; }
        }

        AssetDatabase.Refresh();

        string msg = $"{processed} tilesets processed, {totalSprites} sprites updated " +
                     $"({customShapes.Count} custom shapes, rest full-rect)";
        Debug.Log($"[TilePhysicsSetup] Done — {msg}");
        EditorUtility.DisplayDialog("Custom Physics Shapes Applied", msg, "OK");
    }

    // -----------------------------------------------------------------------
    // Core: apply shapes to one spritesheet
    // -----------------------------------------------------------------------

    /// <summary>
    /// Applies physics shapes to all sprites in a tileset texture.
    /// If customShapes is null, all tiles get full rectangles.
    /// If customShapes is provided, listed tiles get custom shapes; others get full rectangles.
    /// </summary>
    private static int ApplyShapes(string path, Dictionary<int, List<Vector2[]>> customShapes)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return 0;

        if (importer.spriteImportMode != SpriteImportMode.Multiple)
        {
            Debug.LogWarning($"[TilePhysicsSetup] {Path.GetFileNameWithoutExtension(path)}: not sliced — skipping.");
            return 0;
        }

        var factory = new SpriteDataProviderFactories();
        factory.Init();

        var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();

        var physicsProvider = dataProvider.GetDataProvider<ISpritePhysicsOutlineDataProvider>();
        SpriteRect[] spriteRects = dataProvider.GetSpriteRects();

        if (spriteRects == null || spriteRects.Length == 0) return 0;

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

        string tilesetName = Path.GetFileNameWithoutExtension(path);

        foreach (var spriteRect in spriteRects)
        {
            // Extract tile index from sprite name (e.g. "dungeon_5" -> 5)
            int tileIdx = ExtractTileIndex(spriteRect.name);

            if (customShapes != null && tileIdx >= 0 && customShapes.ContainsKey(tileIdx))
            {
                physicsProvider.SetOutlines(spriteRect.spriteID, customShapes[tileIdx]);
            }
            else
            {
                physicsProvider.SetOutlines(spriteRect.spriteID, fullRect);
            }
        }

        dataProvider.Apply();
        importer.SaveAndReimport();

        Debug.Log($"[TilePhysicsSetup] {tilesetName}: {spriteRects.Length} sprites updated.");
        return spriteRects.Length;
    }

    /// <summary>
    /// Extracts the numeric suffix from a sprite name like "dungeon_12" -> 12.
    /// Returns -1 if parsing fails.
    /// </summary>
    private static int ExtractTileIndex(string spriteName)
    {
        int lastUnderscore = spriteName.LastIndexOf('_');
        if (lastUnderscore < 0 || lastUnderscore >= spriteName.Length - 1) return -1;

        string suffix = spriteName.Substring(lastUnderscore + 1);
        return int.TryParse(suffix, out int idx) ? idx : -1;
    }

    // -----------------------------------------------------------------------
    // JSON parsing (minimal, no external dependencies)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parses the physics shapes JSON exported from the HTML painter tool.
    /// Returns a dictionary of tile index -> list of polygon outlines.
    /// Each outline is a Vector2[] in sprite-local pixel coords (-16 to +16).
    /// </summary>
    private static Dictionary<int, List<Vector2[]>> ParseShapeJson(string json)
    {
        var result = new Dictionary<int, List<Vector2[]>>();

        // Use Unity's built-in JSON parsing via a wrapper
        // The format is: { "0": [ {x,y}, ... ], "5": [ {x,y}, ... ] }
        // JsonUtility can't handle this directly, so we parse manually.

        json = json.Trim();
        if (!json.StartsWith("{") || !json.EndsWith("}")) return result;

        // Remove outer braces
        json = json.Substring(1, json.Length - 2).Trim();

        // Split by top-level key entries
        // Find each "N": [ ... ] block
        int pos = 0;
        while (pos < json.Length)
        {
            // Find key
            int keyStart = json.IndexOf('"', pos);
            if (keyStart < 0) break;
            int keyEnd = json.IndexOf('"', keyStart + 1);
            if (keyEnd < 0) break;

            string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);
            if (!int.TryParse(key, out int tileIdx)) { pos = keyEnd + 1; continue; }

            // Find array start
            int arrStart = json.IndexOf('[', keyEnd);
            if (arrStart < 0) break;

            // Find matching array end (handle nested arrays)
            int depth = 0;
            int arrEnd = arrStart;
            for (int i = arrStart; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') { depth--; if (depth == 0) { arrEnd = i; break; } }
            }

            string arrJson = json.Substring(arrStart, arrEnd - arrStart + 1);
            var points = ParsePointArray(arrJson);

            if (points != null && points.Length >= 3)
            {
                result[tileIdx] = new List<Vector2[]> { points };
            }

            pos = arrEnd + 1;
        }

        return result;
    }

    /// <summary>
    /// Parses an array of {x, y} objects into Vector2[].
    /// Input: [ {"x": -16, "y": -16}, {"x": 16, "y": -16}, ... ]
    /// </summary>
    private static Vector2[] ParsePointArray(string json)
    {
        var points = new List<Vector2>();

        int pos = 0;
        while (pos < json.Length)
        {
            int objStart = json.IndexOf('{', pos);
            if (objStart < 0) break;
            int objEnd = json.IndexOf('}', objStart);
            if (objEnd < 0) break;

            string obj = json.Substring(objStart + 1, objEnd - objStart - 1);

            float x = 0, y = 0;
            bool gotX = false, gotY = false;

            // Parse "x": value and "y": value
            foreach (string part in obj.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Contains("\"x\""))
                {
                    string val = trimmed.Split(':')[1].Trim();
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out x))
                        gotX = true;
                }
                else if (trimmed.Contains("\"y\""))
                {
                    string val = trimmed.Split(':')[1].Trim();
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out y))
                        gotY = true;
                }
            }

            if (gotX && gotY)
                points.Add(new Vector2(x, y));

            pos = objEnd + 1;
        }

        return points.Count >= 3 ? points.ToArray() : null;
    }
}
