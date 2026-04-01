using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tools > Paint Wall Faces
///
/// For every chamber prefab across all 10 tilesets:
///   1. Resets Walls TilemapRenderer to the default sprite material (no gradient).
///   2. Creates/updates a "WallBottom" tilemap (sort order 2, WallDepth material)
///      containing ONLY the south-facing wall tiles — wall cells whose south
///      neighbour is floor (has a Ground tile, not a wall tile).
///      The WallDepth shader darkens the bottom edge of these tiles only,
///      creating depth at the wall/floor boundary without touching floor tiles.
///   3. Removes legacy WallFace / WallOutline tilemaps from previous runs.
///
/// Run once after generating chamber prefabs. Safe to re-run.
/// </summary>
public static class PaintWallFacesTool
{
    private const string ShaderName      = "Custom/WallDepth";
    private const string MatPath         = "Assets/Shaders/WallDepth.mat";
    private const string WallBottomName  = "WallBottom";

    [MenuItem("Tools/Paint Wall Faces")]
    public static void PaintAllChambers()
    {
        Material wallDepthMat = GetOrCreateMaterial();
        if (wallDepthMat == null)
        {
            Debug.LogError($"[PaintWallFaces] Could not find shader '{ShaderName}'. " +
                           "Make sure Assets/Shaders/WallDepth.shader is in the project.");
            return;
        }

        // The default Unity sprite material — used to reset Walls back to normal
        Material defaultMat = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Chambers" });
        int processed = 0, skipped = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".prefab")) continue;

            bool changed = ProcessChamberPrefab(path, wallDepthMat, defaultMat);
            if (changed) processed++;
            else skipped++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[PaintWallFaces] Done. Processed={processed}, Skipped={skipped}");
    }

    private static bool ProcessChamberPrefab(string prefabPath, Material wallDepthMat, Material defaultMat)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            Debug.LogWarning($"[PaintWallFaces] Could not load prefab: {prefabPath}");
            return false;
        }

        try
        {
            Transform wallsT  = root.transform.Find("Walls");
            Transform groundT = root.transform.Find("Ground");
            if (wallsT == null || groundT == null) return false;

            Tilemap wallsTm  = wallsT.GetComponent<Tilemap>();
            Tilemap groundTm = groundT.GetComponent<Tilemap>();
            if (wallsTm == null || groundTm == null) return false;

            // --- 1. Reset Walls to default material, sort order 1 ---
            TilemapRenderer wallsRenderer = wallsT.GetComponent<TilemapRenderer>();
            if (wallsRenderer != null)
            {
                if (defaultMat != null) wallsRenderer.material = defaultMat;
                wallsRenderer.sortingOrder = 1;
            }

            // --- 2. Remove legacy tilemaps ---
            foreach (string legacy in new[] { "WallFace", "WallOutline" })
            {
                Transform t = root.transform.Find(legacy);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            // --- 3. Create/clear WallBottom tilemap (order 2, WallDepth material) ---
            Tilemap wallBottomTm = GetOrCreateTilemap(root, WallBottomName, wallsRenderer, 2, wallDepthMat);
            wallBottomTm.ClearAllTiles();

            // --- 4. Populate WallBottom: wall tiles whose south neighbour is floor ---
            BoundsInt bounds = wallsTm.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var wallPos  = new Vector3Int(x, y, 0);
                    var southPos = new Vector3Int(x, y - 1, 0);

                    if (wallsTm.GetTile(wallPos)  == null) continue; // not a wall
                    if (wallsTm.GetTile(southPos) != null) continue; // south is also wall
                    if (groundTm.GetTile(southPos) == null) continue; // south is void (boundary)

                    // Copy this wall tile into WallBottom at the SAME position
                    wallBottomTm.SetTile(wallPos, wallsTm.GetTile(wallPos));
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Tilemap GetOrCreateTilemap(
        GameObject root, string layerName, TilemapRenderer sourceRenderer,
        int sortOrder, Material material)
    {
        Transform t = root.transform.Find(layerName);
        if (t == null)
        {
            var go = new GameObject(layerName);
            go.transform.SetParent(root.transform, false);
            t = go.transform;
            go.AddComponent<Tilemap>();
            var rend = go.AddComponent<TilemapRenderer>();
            if (sourceRenderer != null) rend.sortingLayerID = sourceRenderer.sortingLayerID;
            rend.sortingOrder = sortOrder;
            rend.material     = material;
        }
        else
        {
            var rend = t.GetComponent<TilemapRenderer>();
            if (rend != null)
            {
                rend.sortingOrder = sortOrder;
                rend.material     = material;
            }
        }
        return t.GetComponent<Tilemap>();
    }

    private static Material GetOrCreateMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (existing != null) return existing;

        Shader shader = Shader.Find(ShaderName);
        if (shader == null) return null;

        var mat = new Material(shader);
        mat.SetFloat("_ShadowStrength", 0.6f);
        mat.SetFloat("_ShadowFade",     0.45f);
        AssetDatabase.CreateAsset(mat, MatPath);
        return mat;
    }
}
