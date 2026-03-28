using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Assembles a floor from 8 chamber prefabs arranged in a 4x2 grid (80x40 world units).
///
/// Setup:
/// 1. Add this component to an empty GameObject in your scene.
/// 2. In Tileset Libraries, add one entry per tileset (e.g. "dungeon", "flames").
///    Under each entry, add the 8 chamber prefabs for that tileset.
/// 3. Wire EnemySpawner reference.
/// 4. Drive assembly via StageLoader (assembleOnStart should stay OFF).
/// </summary>
public class FloorAssembler : MonoBehaviour
{
    [System.Serializable]
    public class ChamberEntry
    {
        public string     id;
        public GameObject prefab;
    }

    [System.Serializable]
    public class TilesetLibrary
    {
        public string           tilesetId;
        public List<ChamberEntry> chambers = new List<ChamberEntry>();
    }

    [Header("Tileset Libraries — one entry per tileset, each with its 8 chamber prefabs")]
    public List<TilesetLibrary> tilesetLibraries = new List<TilesetLibrary>();

    [Header("Enemy Spawner — optional, called after map assembly")]
    public EnemySpawner enemySpawner;

    [Header("Enemy Spawns — filled at runtime from Floor Manifest")]
    public List<EnemySpawnDTO> enemySpawns = new List<EnemySpawnDTO>();

    [Header("Active Tileset — set from Floor Manifest at runtime")]
    public string activeTilesetId = "dungeon";

    [Header("Grid — 8 chamber IDs, row-major (index 0-3 = bottom row, 4-7 = top row)")]
    [Tooltip(
        "Visual layout:\n" +
        "[4][5][6][7]  ← top row    (y = 20)\n" +
        "[0][1][2][3]  ← bottom row (y =  0)")]
    public string[] chamberGrid = new string[8];

    [Header("Auto-assemble")]
    [Tooltip("Assemble the floor automatically on Start using the Inspector values above. Disable when driving from StageLoader.")]
    public bool assembleOnStart = false;

    private const int ChamberSize = 20;  // world units per chamber (matches 20x20 tilemap)

    private void Start()
    {
        if (assembleOnStart)
            AssembleFloor();
    }

    // --- JSON / manifest-driven entry points ---

    /// <summary>
    /// Applies a parsed FloorManifestDTO to this assembler and rebuilds the floor.
    /// Sets the active tileset from the manifest so only matching chambers are used.
    /// </summary>
    public void LoadManifest(FloorManifestDTO manifest)
    {
        if (!string.IsNullOrEmpty(manifest.tileset_id))
            activeTilesetId = manifest.tileset_id;

        if (manifest.chamber_grid != null)
            chamberGrid = manifest.chamber_grid;

        enemySpawns.Clear();
        if (manifest.enemy_spawns != null)
            enemySpawns.AddRange(manifest.enemy_spawns);

        AssembleFloor();
    }

    /// <summary>
    /// Parses a Floor Manifest JSON string and rebuilds the floor.
    /// </summary>
    public void LoadFromJson(string json)
    {
        var manifest = JsonUtility.FromJson<FloorManifestDTO>(json);
        if (manifest == null)
        {
            Debug.LogError("FloorAssembler.LoadFromJson: failed to parse manifest JSON.");
            return;
        }
        LoadManifest(manifest);
    }

    public void AssembleFloor()
    {
        // Clear any previously assembled chambers
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        var library = BuildLibrary(activeTilesetId);

        for (int i = 0; i < 8; i++)
        {
            if (i >= chamberGrid.Length) break;
            string id = chamberGrid[i];
            if (string.IsNullOrEmpty(id)) continue;

            if (!library.TryGetValue(id, out GameObject prefab))
            {
                Debug.LogWarning($"FloorAssembler: chamber '{id}' not found in tileset '{activeTilesetId}' — skipping.");
                continue;
            }

            int col = i % 4;
            int row = i / 4;
            Vector3 worldOffset = transform.position + new Vector3(col * ChamberSize, row * ChamberSize, 0f);

            Instantiate(prefab, worldOffset, Quaternion.identity, transform);
        }

        PositionMapBounds();
        SpawnBoundaryWalls();

        if (enemySpawner != null && enemySpawns != null && enemySpawns.Count > 0)
            enemySpawner.SpawnFloor(enemySpawns, transform.position);
    }

    private void SpawnBoundaryWalls()
    {
        float mapW = ChamberSize * 4;
        float mapH = ChamberSize * 2;
        float cx   = transform.position.x + mapW / 2f;
        float cy   = transform.position.y + mapH / 2f;
        float thickness = 1f;

        CreateWall("Boundary_Bottom", new Vector2(cx, transform.position.y - thickness / 2f), new Vector2(mapW, thickness));
        CreateWall("Boundary_Top",    new Vector2(cx, transform.position.y + mapH + thickness / 2f), new Vector2(mapW, thickness));
        CreateWall("Boundary_Left",   new Vector2(transform.position.x - thickness / 2f, cy), new Vector2(thickness, mapH));
        CreateWall("Boundary_Right",  new Vector2(transform.position.x + mapW + thickness / 2f, cy), new Vector2(thickness, mapH));
    }

    private void CreateWall(string wallName, Vector2 position, Vector2 size)
    {
        var go = new GameObject(wallName);
        go.transform.SetParent(transform);
        go.transform.position = position;
        var col = go.AddComponent<BoxCollider2D>();
        col.size = size;
    }

    /// <summary>
    /// Builds a lookup of id → prefab for the given tileset only.
    /// Chamber IDs from other tilesets are excluded, preventing cross-tileset mixing.
    /// </summary>
    private Dictionary<string, GameObject> BuildLibrary(string tilesetId)
    {
        var lib = new Dictionary<string, GameObject>();
        foreach (var tileset in tilesetLibraries)
        {
            if (tileset.tilesetId != tilesetId) continue;
            foreach (var entry in tileset.chambers)
                if (entry.prefab != null && !string.IsNullOrEmpty(entry.id))
                    lib[entry.id] = entry.prefab;
        }

        if (lib.Count == 0)
            Debug.LogWarning($"FloorAssembler: no chambers registered for tileset '{tilesetId}'.");

        return lib;
    }

    private void PositionMapBounds()
    {
        var marker = FindObjectOfType<MapBoundsMarker>();
        if (marker == null) return;

        marker.transform.position = transform.position + new Vector3(
            ChamberSize * 4 / 2f,
            ChamberSize * 2 / 2f,
            0f);
        marker.size = new Vector2(ChamberSize * 4, ChamberSize * 2);
    }

#if UNITY_EDITOR
    [ContextMenu("Assemble Floor (Editor Preview)")]
    private void AssembleInEditor()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            UnityEditor.Undo.DestroyObjectImmediate(transform.GetChild(i).gameObject);

        var library = BuildLibrary(activeTilesetId);

        for (int i = 0; i < 8; i++)
        {
            if (i >= chamberGrid.Length) break;
            string id = chamberGrid[i];
            if (string.IsNullOrEmpty(id)) continue;

            if (!library.TryGetValue(id, out GameObject prefab))
            {
                Debug.LogWarning($"FloorAssembler: chamber '{id}' not found in tileset '{activeTilesetId}' — skipping.");
                continue;
            }

            int col = i % 4;
            int row = i / 4;
            Vector3 offset = transform.position + new Vector3(col * ChamberSize, row * ChamberSize, 0f);

            var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
            go.transform.position = offset;
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Assemble Floor");
        }

        PositionMapBounds();
    }
#endif
}
