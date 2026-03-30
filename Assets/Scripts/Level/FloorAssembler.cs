using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

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
    /// <summary>
    /// Fired at the end of AssembleFloor(). Passes the world-space origin of the map
    /// (i.e. transform.position — the bottom-left of chamber [0]).
    /// Subscribe in Awake, unsubscribe in OnDestroy.
    /// </summary>
    public static event System.Action<Vector3> OnFloorReady;
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
    public string   activeTilesetId = "dungeon";

    [Header("Active Lamp IDs — set from Floor Manifest at runtime (used by DecoSpawner)")]
    public string[] activeLampIds;

    [Header("Grid — 8 chamber IDs, row-major (index 0-3 = bottom row, 4-7 = top row)")]
    [Tooltip(
        "Visual layout:\n" +
        "[4][5][6][7]  ← top row    (y = 20)\n" +
        "[0][1][2][3]  ← bottom row (y =  0)")]
    public string[] chamberGrid = new string[8];

    [Header("Tilemap Tints")]
    public Color groundTint = new Color(0.45f, 0.45f, 0.45f, 1f);
    public Color wallTint   = new Color(1.0f,  0.85f, 0.7f,  1f);

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
        {
            // Normalise to lowercase — Gemini may return "Dungeon" instead of "dungeon".
            string requested = manifest.tileset_id.Trim().ToLowerInvariant();

            // Validate the tileset exists; fall back to previous tileset if unknown.
            bool found = false;
            foreach (var lib in tilesetLibraries)
                if (lib.tilesetId == requested) { found = true; break; }

            if (found)
                activeTilesetId = requested;
            else
                Debug.LogWarning($"FloorAssembler: unknown tileset '{requested}' from manifest — keeping '{activeTilesetId}'.");
        }

        // If the manifest includes a chamber grid (e.g. hardcoded Stage 1), use it.
        // Otherwise randomize 8 chambers from the active tileset (repetition allowed).
        chamberGrid = (manifest.chamber_grid != null && manifest.chamber_grid.Length > 0)
            ? manifest.chamber_grid
            : RandomizeChamberGrid(activeTilesetId);

        activeLampIds = manifest.lamp_ids;

        enemySpawns.Clear();
        if (manifest.enemy_spawns != null)
            enemySpawns.AddRange(manifest.enemy_spawns);

        AssembleFloor();
    }

    /// <summary>
    /// Picks 8 chamber IDs at random from the registered chambers for the given tileset.
    /// Each slot is chosen independently, so chambers can repeat and some may go unused.
    /// </summary>
    private string[] RandomizeChamberGrid(string tilesetId)
    {
        var library = BuildLibrary(tilesetId);
        var ids = new System.Collections.Generic.List<string>(library.Keys);
        if (ids.Count == 0)
        {
            Debug.LogWarning($"FloorAssembler: no chambers available for tileset '{tilesetId}' — grid will be empty.");
            return new string[8];
        }

        var grid = new string[8];
        for (int i = 0; i < 8; i++)
            grid[i] = ids[Random.Range(0, ids.Count)];
        return grid;
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
        // Clear any previously assembled chambers.
        // Deactivate first so old wall colliders are removed from physics
        // immediately, then Destroy for proper cleanup at end of frame.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }

        var library = BuildLibrary(activeTilesetId);
        Debug.Log($"[FloorAssembler] AssembleFloor: tileset='{activeTilesetId}', " +
                  $"library has {library.Count} chambers, " +
                  $"grid=[{string.Join(", ", chamberGrid)}]");

        int chambersPlaced = 0;
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

            var chamber = Instantiate(prefab, worldOffset, Quaternion.identity, transform);
            ApplyGroundTint(chamber);
            ConfigureChamberLayers(chamber);
            chambersPlaced++;
        }

        Debug.Log($"[FloorAssembler] Placed {chambersPlaced}/8 chambers.");
        if (chambersPlaced == 0)
            Debug.LogError("[FloorAssembler] NO CHAMBERS PLACED — map will be empty! " +
                           $"Check tileset '{activeTilesetId}' has registered prefabs (Tools > Populate Chamber Library).");

        PositionMapBounds();
        SpawnBoundaryWalls();

        if (AstarPath.active != null)
        {
            float mapW = ChamberSize * 4;
            float mapH = ChamberSize * 2;
            Vector3 mapCenter = transform.position + new Vector3(mapW / 2f, mapH / 2f, 0f);

            bool foundGrid = false;
            foreach (var graph in AstarPath.active.data.graphs)
            {
                if (graph is Pathfinding.GridGraph gridGraph)
                {
                    foundGrid = true;
                    gridGraph.center = mapCenter;
                    gridGraph.SetDimensions(
                        Mathf.RoundToInt(mapW / gridGraph.nodeSize),
                        Mathf.RoundToInt(mapH / gridGraph.nodeSize),
                        gridGraph.nodeSize);

                    // Force 2D mode — sets rotation to XY plane and enables 2D physics
                    gridGraph.is2D = true;
                    gridGraph.collision.use2D = true;
                    gridGraph.collision.collisionCheck = true;
                    gridGraph.collision.mask = LayerMask.GetMask("Walls");
                    gridGraph.collision.diameter = 2.0f;
                    gridGraph.collision.type = Pathfinding.ColliderType.Sphere;
                    gridGraph.cutCorners = false;
                    gridGraph.erodeIterations = 2;
                }
            }

            if (!foundGrid)
                Debug.LogWarning("FloorAssembler: AstarPath is active but has NO GridGraph — pathfinding will not work. Add a GridGraph in the A* Inspector.");

            AstarPath.active.logPathResults = Pathfinding.PathLog.None;

            // Force all CompositeCollider2D on Walls tilemaps to rebuild immediately,
            // otherwise the A* scan runs before Unity's physics step and sees no walls.
            Physics2D.SyncTransforms();
            foreach (var composite in FindObjectsByType<CompositeCollider2D>(FindObjectsSortMode.None))
                composite.GenerateGeometry();

            AstarPath.active.Scan();

            // Count walkable vs unwalkable nodes
            int walkable = 0, unwalkable = 0;
            foreach (var graph in AstarPath.active.data.graphs)
            {
                if (graph is Pathfinding.GridGraph scanned)
                {
                    scanned.GetNodes(node => { if (node.Walkable) walkable++; else unwalkable++; });
                }
            }
            Debug.Log($"FloorAssembler: A* scan complete — walkable={walkable}, unwalkable={unwalkable}, total={walkable + unwalkable}");
        }
        else
        {
            Debug.LogWarning("FloorAssembler: AstarPath.active is NULL — no Pathfinder object in scene. Enemies will not pathfind.");
        }

        if (enemySpawner != null && enemySpawns != null && enemySpawns.Count > 0)
            enemySpawner.SpawnFloor(enemySpawns, transform.position);

        OnFloorReady?.Invoke(transform.position);
    }

    private void ApplyGroundTint(GameObject chamber)
    {
        var ground = chamber.transform.Find("Ground");
        if (ground != null)
        {
            var tm = ground.GetComponent<Tilemap>();
            if (tm != null) tm.color = groundTint;
        }

        foreach (string layer in new[] { "Walls", "Details" })
        {
            var t = chamber.transform.Find(layer);
            if (t == null) continue;
            var tm = t.GetComponent<Tilemap>();
            if (tm != null) tm.color = wallTint;
        }
    }

    private void ConfigureChamberLayers(GameObject chamber)
    {
        int wallLayer   = LayerMask.NameToLayer("Walls");
        int groundLayer = LayerMask.NameToLayer("Ground");

        var walls = chamber.transform.Find("Walls");
        if (walls != null)
        {
            walls.gameObject.layer = wallLayer;

            // Ensure CompositeCollider2D uses filled polygons, not just outlines
            var composite = walls.GetComponent<CompositeCollider2D>();
            if (composite != null)
                composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        }

        var ground = chamber.transform.Find("Ground");
        if (ground != null)
            ground.gameObject.layer = groundLayer;

        var details = chamber.transform.Find("Details");
        if (details != null)
            details.gameObject.layer = groundLayer;
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
        var marker = FindAnyObjectByType<MapBoundsMarker>();
        if (marker == null) return;

        marker.transform.position = transform.position + new Vector3(
            ChamberSize * 4 / 2f,
            ChamberSize * 2 / 2f,
            0f);
        marker.size = new Vector2(ChamberSize * 4, ChamberSize * 2);
        marker.SyncConfiner();
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
            ApplyGroundTint(go);
        }

        PositionMapBounds();
    }
#endif
}
