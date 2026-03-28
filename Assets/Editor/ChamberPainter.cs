using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;

/// <summary>
/// Reads Assets/Chambers/<tileset>/chambers.json and generates prefabs for each chamber.
/// Run via: Tools > Generate Chamber Prefabs
/// </summary>
public static class ChamberPainter
{
    private const string ChambersRoot = "Assets/Chambers";
    private const string TilesRoot    = "Assets/Art/Spritesheets/Tilesets/Palettes";

    // ── JSON data model ───────────────────────────────────────────────────────

    [Serializable]
    private class TileClassification
    {
        public int   primary_floor = 0;
        public int   primary_wall  = 0;
        public int   alt_floor     = -1;
        public int[] detail_tiles;
    }

    [Serializable]
    private class ChamberData
    {
        public string   id;
        public string   description;
        public string[] tags;
        public string[] layout;
    }

    [Serializable]
    private class ChamberManifest
    {
        public string             tileset_id;
        public TileClassification tile_classification;
        public ChamberData[]      chambers;
    }

    // ── Menu entry ────────────────────────────────────────────────────────────

    [MenuItem("Tools/Generate Chamber Prefabs")]
    public static void GenerateAll()
    {
        if (!AssetDatabase.IsValidFolder(ChambersRoot))
        {
            Debug.LogError($"ChamberPainter: folder '{ChambersRoot}' not found. Run generate_chambers.py first.");
            return;
        }

        string[] jsonFiles = Directory.GetFiles(
            Path.Combine(Application.dataPath, "Chambers"),
            "chambers.json",
            SearchOption.AllDirectories
        );

        int total = 0;
        foreach (string absPath in jsonFiles)
        {
            string json = File.ReadAllText(absPath);
            var manifest = JsonUtility.FromJson<ChamberManifest>(json);
            if (manifest?.chambers == null) { Debug.LogWarning($"Skipping invalid JSON: {absPath}"); continue; }

            int count = ProcessManifest(manifest);
            total += count;
            Debug.Log($"[{manifest.tileset_id}] Generated {count} prefabs.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Done — {total} chamber prefabs generated.");
    }

    // ── Per-tileset processing ────────────────────────────────────────────────

    private static int ProcessManifest(ChamberManifest manifest)
    {
        string tileset    = manifest.tileset_id;
        string tilesDir   = $"{TilesRoot}/{CapFirst(tileset)}";
        string prefabsDir = $"{ChambersRoot}/{tileset}/Prefabs";

        EnsureFolder(prefabsDir);

        var tc = manifest.tile_classification;

        TileBase floorTile  = LoadTile(tilesDir, tileset, tc.primary_floor);
        TileBase wallTile   = LoadTile(tilesDir, tileset, tc.primary_wall);
        TileBase altTile    = tc.alt_floor >= 0 ? LoadTile(tilesDir, tileset, tc.alt_floor) : floorTile;
        TileBase detailTile = floorTile;

        if (tc.detail_tiles != null && tc.detail_tiles.Length > 0)
            detailTile = LoadTile(tilesDir, tileset, tc.detail_tiles[0]) ?? floorTile;

        int count = 0;
        foreach (var chamber in manifest.chambers)
        {
            if (string.IsNullOrEmpty(chamber.id) || chamber.layout == null) continue;
            CreateChamberPrefab(chamber, floorTile, wallTile, altTile, detailTile, prefabsDir);
            count++;
        }
        return count;
    }

    // ── Prefab creation ───────────────────────────────────────────────────────

    private static void CreateChamberPrefab(
        ChamberData chamber,
        TileBase floorTile, TileBase wallTile, TileBase altTile, TileBase detailTile,
        string prefabsDir)
    {
        var root = new GameObject(chamber.id);
        root.AddComponent<Grid>();  // Tilemap requires a Grid parent

        // Ground layer
        var groundGO       = new GameObject("Ground");
        groundGO.transform.SetParent(root.transform, false);
        var groundTilemap  = groundGO.AddComponent<Tilemap>();
        var groundRenderer = groundGO.AddComponent<TilemapRenderer>();
        groundRenderer.sortingLayerName = "Background";
        groundRenderer.sortingOrder     = 0;

        // Walls layer  (TilemapLayerAutoAssign will set the physics layer automatically)
        var wallsGO       = new GameObject("Walls");
        wallsGO.transform.SetParent(root.transform, false);
        var wallsTilemap  = wallsGO.AddComponent<Tilemap>();
        var wallsRenderer = wallsGO.AddComponent<TilemapRenderer>();
        wallsRenderer.sortingLayerName = "Background";
        wallsRenderer.sortingOrder     = 1;

        wallsTilemap.color = new Color(0.25f, 0.22f, 0.22f, 1f);  // dark tint to contrast with floor

        var wallsCollider           = wallsGO.AddComponent<TilemapCollider2D>();
        wallsCollider.usedByComposite = true;
        var rb                      = wallsGO.AddComponent<Rigidbody2D>();
        rb.bodyType                 = RigidbodyType2D.Static;
        wallsGO.AddComponent<CompositeCollider2D>();

        // Details layer
        var detailsGO       = new GameObject("Details");
        detailsGO.transform.SetParent(root.transform, false);
        var detailsTilemap  = detailsGO.AddComponent<Tilemap>();
        var detailsRenderer = detailsGO.AddComponent<TilemapRenderer>();
        detailsRenderer.sortingLayerName = "Foreground";
        detailsRenderer.sortingOrder     = 0;

        // Paint tiles from layout
        // Row 0 = south (bottom), row 19 = north (top) — matches Unity's Y-up axis
        for (int row = 0; row < 20 && row < chamber.layout.Length; row++)
        {
            string rowStr = chamber.layout[row];
            for (int col = 0; col < 20 && col < rowStr.Length; col++)
            {
                var pos = new Vector3Int(col, row, 0);
                switch (rowStr[col])
                {
                    case 'F':
                        groundTilemap.SetTile(pos, floorTile);
                        break;
                    case 'W':
                        groundTilemap.SetTile(pos, floorTile);   // floor underneath wall
                        wallsTilemap.SetTile(pos, wallTile);
                        break;
                    case 'D':
                        groundTilemap.SetTile(pos, floorTile);
                        detailsTilemap.SetTile(pos, detailTile);
                        break;
                    case 'O':
                        // intentional void — no tile
                        break;
                }
            }
        }

        string prefabPath = $"{prefabsDir}/{chamber.id}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TileBase LoadTile(string dir, string tileset, int index)
    {
        string path = $"{dir}/{tileset}_{index}.asset";
        var tile = AssetDatabase.LoadAssetAtPath<TileBase>(path);
        if (tile == null) Debug.LogWarning($"ChamberPainter: tile not found at {path}");
        return tile;
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath)) return;

        string parent = assetPath.Substring(0, assetPath.LastIndexOf('/'));
        string name   = assetPath.Substring(assetPath.LastIndexOf('/') + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static string CapFirst(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
}
