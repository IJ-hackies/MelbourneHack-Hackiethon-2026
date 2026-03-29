using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;

/// <summary>
/// Reads Assets/Chambers/<tileset>/chambers.json and generates prefabs for each chamber.
/// Supports two layout formats:
///   - "hex" (new): each char is 0-9/a-f = tile index, x = void
///   - legacy: chars F,W,A,B,D,E,G,H,O mapped to tile indices via tile_classification
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
        // New hex format fields
        public int[] wall_indices;
        public int[] detail_indices;
        public int   floor_base = 5;

        // Legacy format fields (used as fallback)
        public int   primary_floor = 5;
        public int   primary_wall  = 0;
        public int   alt_floor     = 6;
        public int   accent_wall   = 3;
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
        public string             format;          // "hex" or absent for legacy
        public TileClassification tile_classification;
        public ChamberData[]      chambers;
    }

    // ── Menu entry ────────────────────────────────────────────────────────────

    [MenuItem("Tools/Generate Chamber Prefabs")]
    public static void GenerateAll()
    {
        if (!AssetDatabase.IsValidFolder(ChambersRoot))
        {
            Debug.LogError($"ChamberPainter: folder '{ChambersRoot}' not found.");
            return;
        }

        // Find both "chambers.json" and "*_chambers.json" files
        string[] jsonFiles = Directory.GetFiles(
            Path.Combine(Application.dataPath, "Chambers"),
            "*chambers.json",
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
        Debug.Log($"Done - {total} chamber prefabs generated.");
    }

    // ── Per-tileset processing ────────────────────────────────────────────────

    private static int ProcessManifest(ChamberManifest manifest)
    {
        string tileset    = manifest.tileset_id;
        string tilesDir   = $"{TilesRoot}/{CapFirst(tileset)}";
        string prefabsDir = $"{ChambersRoot}/{tileset}/Prefabs";
        bool   isHex      = manifest.format == "hex";

        EnsureFolder(prefabsDir);

        // Load all 16 tiles
        TileBase[] tiles = new TileBase[16];
        for (int i = 0; i < 16; i++)
            tiles[i] = LoadTile(tilesDir, tileset, i);

        // Determine which indices are walls, details, and the floor base
        var tc = manifest.tile_classification;
        HashSet<int> wallSet   = new HashSet<int>();
        HashSet<int> detailSet = new HashSet<int>();
        int floorBaseIdx = 5;

        if (isHex)
        {
            floorBaseIdx = tc.floor_base;
            // Everything except the floor base is a wall
            for (int i = 0; i < 16; i++)
                if (i != floorBaseIdx) wallSet.Add(i);
        }
        else
        {
            // Legacy: derive from old classification fields
            wallSet.Add(tc.primary_wall);
            if (tc.accent_wall >= 0) wallSet.Add(tc.accent_wall);
            if (tc.detail_tiles != null) foreach (int d in tc.detail_tiles) detailSet.Add(d);
            floorBaseIdx = tc.primary_floor;
        }

        int count = 0;
        foreach (var chamber in manifest.chambers)
        {
            if (string.IsNullOrEmpty(chamber.id) || chamber.layout == null) continue;

            if (isHex)
                CreateHexChamberPrefab(chamber, tiles, wallSet, detailSet, floorBaseIdx, prefabsDir);
            else
                CreateLegacyChamberPrefab(chamber, tiles, tc, prefabsDir);

            count++;
        }
        return count;
    }

    // ── Hex format prefab creation ───────────────────────────────────────────

    private static void CreateHexChamberPrefab(
        ChamberData chamber, TileBase[] tiles,
        HashSet<int> wallSet, HashSet<int> detailSet, int floorBaseIdx,
        string prefabsDir)
    {
        var root = new GameObject(chamber.id);
        root.AddComponent<Grid>();

        var (groundTM, _, _)  = MakeLayer(root, "Ground",  "Background", 0, false);
        var (wallsTM, _, _)   = MakeLayer(root, "Walls",   "Background", 1, true);
        var (detailsTM, _, _) = MakeLayer(root, "Details", "Foreground", 0, false);

        wallsTM.color = new Color(0.25f, 0.22f, 0.22f, 1f);
        TileBase floorBase = tiles[floorBaseIdx];

        // In hex format, row 0 = top (north) in the painter, but Unity Y=0 is bottom (south).
        // Flip rows so the layout matches what the user designed visually.
        for (int row = 0; row < 20 && row < chamber.layout.Length; row++)
        {
            string rowStr = chamber.layout[row];
            for (int col = 0; col < 20 && col < rowStr.Length; col++)
            {
                char ch = rowStr[col];
                if (ch == 'x' || ch == 'X') continue; // void

                int tileIdx = HexCharToInt(ch);
                if (tileIdx < 0 || tileIdx >= 16 || tiles[tileIdx] == null) continue;

                var pos = new Vector3Int(col, 19 - row, 0);

                if (wallSet.Contains(tileIdx))
                {
                    if (floorBase != null) groundTM.SetTile(pos, floorBase);
                    wallsTM.SetTile(pos, tiles[tileIdx]);
                }
                else if (detailSet.Contains(tileIdx))
                {
                    if (floorBase != null) groundTM.SetTile(pos, floorBase);
                    detailsTM.SetTile(pos, tiles[tileIdx]);
                }
                else
                {
                    groundTM.SetTile(pos, tiles[tileIdx]);
                }
            }
        }

        string prefabPath = $"{prefabsDir}/{chamber.id}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
    }

    // ── Legacy letter format (backward compat) ───────────────────────────────

    private static void CreateLegacyChamberPrefab(
        ChamberData chamber, TileBase[] tiles, TileClassification tc,
        string prefabsDir)
    {
        TileBase floorTile      = tiles[tc.primary_floor];
        TileBase wallTile       = tiles[tc.primary_wall];
        TileBase altTile        = tc.alt_floor >= 0 ? tiles[tc.alt_floor] : floorTile;
        TileBase accentWallTile = tc.accent_wall >= 0 ? tiles[tc.accent_wall] : wallTile;
        TileBase detailTile  = (tc.detail_tiles != null && tc.detail_tiles.Length > 0) ? tiles[tc.detail_tiles[0]] ?? floorTile : floorTile;
        TileBase detailTile2 = (tc.detail_tiles != null && tc.detail_tiles.Length > 1) ? tiles[tc.detail_tiles[1]] ?? floorTile : floorTile;
        TileBase detailTile3 = (tc.detail_tiles != null && tc.detail_tiles.Length > 2) ? tiles[tc.detail_tiles[2]] ?? floorTile : floorTile;
        TileBase detailTile4 = (tc.detail_tiles != null && tc.detail_tiles.Length > 3) ? tiles[tc.detail_tiles[3]] ?? floorTile : floorTile;

        var root = new GameObject(chamber.id);
        root.AddComponent<Grid>();

        var (groundTM, _, _)  = MakeLayer(root, "Ground",  "Background", 0, false);
        var (wallsTM, _, _)   = MakeLayer(root, "Walls",   "Background", 1, true);
        var (detailsTM, _, _) = MakeLayer(root, "Details", "Foreground", 0, false);

        wallsTM.color = new Color(0.25f, 0.22f, 0.22f, 1f);

        for (int row = 0; row < 20 && row < chamber.layout.Length; row++)
        {
            string rowStr = chamber.layout[row];
            for (int col = 0; col < 20 && col < rowStr.Length; col++)
            {
                var pos = new Vector3Int(col, row, 0);
                switch (rowStr[col])
                {
                    case 'F': groundTM.SetTile(pos, floorTile); break;
                    case 'W': groundTM.SetTile(pos, floorTile); wallsTM.SetTile(pos, wallTile); break;
                    case 'A': groundTM.SetTile(pos, altTile); break;
                    case 'B': groundTM.SetTile(pos, floorTile); wallsTM.SetTile(pos, accentWallTile); break;
                    case 'D': groundTM.SetTile(pos, floorTile); detailsTM.SetTile(pos, detailTile); break;
                    case 'E': groundTM.SetTile(pos, floorTile); detailsTM.SetTile(pos, detailTile2); break;
                    case 'G': groundTM.SetTile(pos, floorTile); detailsTM.SetTile(pos, detailTile3); break;
                    case 'H': groundTM.SetTile(pos, floorTile); detailsTM.SetTile(pos, detailTile4); break;
                    case 'O': break; // void
                }
            }
        }

        string prefabPath = $"{prefabsDir}/{chamber.id}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (Tilemap, TilemapRenderer, GameObject) MakeLayer(
        GameObject root, string name, string sortingLayer, int sortingOrder, bool addCollider)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        var tm = go.AddComponent<Tilemap>();
        var rend = go.AddComponent<TilemapRenderer>();
        rend.sortingLayerName = sortingLayer;
        rend.sortingOrder = sortingOrder;

        if (addCollider)
        {
            var col = go.AddComponent<TilemapCollider2D>();
            col.usedByComposite = true;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;
            go.AddComponent<CompositeCollider2D>();
        }

        return (tm, rend, go);
    }

    private static int HexCharToInt(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return 10 + c - 'a';
        if (c >= 'A' && c <= 'F') return 10 + c - 'A';
        return -1;
    }

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
