using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Spawns lamps and decorations on every chamber's Ground tilemap after floor assembly.
/// Subscribes to FloorAssembler.OnFloorReady and reads activeTilesetId + activeLampIds from FloorAssembler.
///
/// Setup:
///   1. Add this component to a GameObject in the scene (e.g. the FloorAssembler object).
///   2. Wire the FloorAssembler reference.
///   3. Drag all lamp and deco prefabs into the respective Inspector slots.
/// </summary>
public class DecoSpawner : MonoBehaviour
{
    // ── Lamp prefabs ──────────────────────────────────────────────────────────
    [Header("Lamp Prefabs")]
    public GameObject BlueLampPrefab;
    public GameObject GreenLampPrefab;
    public GameObject OceanBlueLampPrefab;
    public GameObject PinkLampPrefab;
    public GameObject RedLampPrefab;
    public GameObject TechLampPrefab;
    public GameObject YellowLampPrefab;

    // ── Deco prefabs ──────────────────────────────────────────────────────────
    [Header("Cosmetic Deco Prefabs")]
    public GameObject BarrelsPrefab;
    public GameObject BonePilePrefab;
    public GameObject WaterPoolPrefab;
    public GameObject PotPrefab;

    [Header("Debuffing Deco Prefabs")]
    public GameObject AcidPoolPrefab;
    public GameObject CobwebPrefab;

    [Header("Pickup Prefabs")]
    public GameObject HealScrollPrefab;

    // ── References ────────────────────────────────────────────────────────────
    [Header("References")]
    public FloorAssembler floorAssembler;

    // ── Biome → default lamp table ────────────────────────────────────────────
    private static readonly Dictionary<string, string[]> BiomeLampTable =
        new Dictionary<string, string[]>
        {
            { "dungeon",   new[] { "RedLamp",       "YellowLamp"    } },
            { "flames",    new[] { "RedLamp",        "OceanBlueLamp" } },
            { "forest",    new[] { "GreenLamp",      "YellowLamp"    } },
            { "frozen",    new[] { "BlueLamp",       "OceanBlueLamp" } },
            { "ocean",     new[] { "OceanBlueLamp",  "BlueLamp"      } },
            { "acid",      new[] { "GreenLamp",      "YellowLamp"    } },
            { "techy",     new[] { "TechLamp",       "BlueLamp"      } },
            { "bubblegum", new[] { "PinkLamp",       "YellowLamp"    } },
            { "honey",     new[] { "YellowLamp",     "OceanBlueLamp" } },
            { "rocky",     new[] { "RedLamp",        "YellowLamp"    } },
        };

    private const string FallbackLamp = "RedLamp";

    // ── Spawn quantity constants ──────────────────────────────────────────────
    private const int LampsMin      = 1;
    private const int LampsMax      = 2;
    private const int DecosMin      = 4;
    private const int DecosMax      = 6;
    private const int DebuffingMax  = 2;

    // ── Corridor-exclusion constants (0-indexed, 20-tile-wide chamber) ────────
    // Openings sit at local tiles 9–10 on each edge; exclude a buffer around them.
    private const int OpenMin       = 7;   // first tile of exclusion window around opening
    private const int OpenMax       = 12;  // last tile
    private const int EdgeDepth     = 3;   // inward depth of corridor exclusion
    private const int BorderMargin  = 2;   // don't place within this many tiles of any edge

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        FloorAssembler.OnFloorReady += OnFloorReady;
    }

    private void OnDestroy()
    {
        FloorAssembler.OnFloorReady -= OnFloorReady;
    }

    // ── Main spawn entry point ────────────────────────────────────────────────

    private void OnFloorReady(Vector3 origin)
    {
        if (floorAssembler == null)
        {
            Debug.LogWarning("[DecoSpawner] FloorAssembler reference not set.");
            return;
        }

        var lampPrefabs = ResolveLamps();
        if (lampPrefabs.Count == 0)
        {
            Debug.LogWarning("[DecoSpawner] No lamp prefabs resolved — skipping lamp placement.");
        }

        // Iterate chamber children (boundary walls have no Ground tilemap child)
        var allValidPositions = new List<Vector3>();
        foreach (Transform child in floorAssembler.transform)
        {
            var groundTm = child.Find("Ground")?.GetComponent<Tilemap>();
            if (groundTm == null) continue;

            SpawnInChamber(child, groundTm, lampPrefabs);

            // Collect valid positions for heal scroll placement (second pass below)
            if (HealScrollPrefab != null && floorAssembler.activeHealScrollCount > 0)
            {
                var wallsTm = child.Find("Walls")?.GetComponent<Tilemap>();
                var positions = CollectValidPositions(groundTm, wallsTm);
                allValidPositions.AddRange(positions);
            }
        }

        // Spawn heal scrolls distributed randomly across the floor
        if (floorAssembler.activeHealScrollCount > 0)
        {
            if (HealScrollPrefab == null)
            {
                Debug.LogWarning("[DecoSpawner] activeHealScrollCount > 0 but HealScrollPrefab is not assigned — wire it in the Inspector.");
            }
            else if (allValidPositions.Count > 0)
            {
                Shuffle(allValidPositions);
                int count = Mathf.Min(floorAssembler.activeHealScrollCount, allValidPositions.Count);
                for (int i = 0; i < count; i++)
                    Instantiate(HealScrollPrefab, allValidPositions[i], Quaternion.identity, floorAssembler.transform);
            }
        }
    }

    // ── Lamp resolution ───────────────────────────────────────────────────────

    private List<GameObject> ResolveLamps()
    {
        var result = new List<GameObject>();

        // 1. Use manifest lamp_ids from FloorAssembler if provided
        string[] ids = floorAssembler.activeLampIds;
        if (ids != null && ids.Length > 0)
        {
            foreach (var id in ids)
            {
                var p = GetLampPrefab(id);
                if (p != null) result.Add(p);
            }
        }

        // 2. Fall back to biome table
        if (result.Count == 0)
        {
            string tileset = floorAssembler.activeTilesetId;
            if (BiomeLampTable.TryGetValue(tileset, out string[] defaults))
            {
                int count = Random.Range(1, 3); // 1 or 2
                for (int i = 0; i < Mathf.Min(count, defaults.Length); i++)
                {
                    var p = GetLampPrefab(defaults[i]);
                    if (p != null) result.Add(p);
                }
            }
        }

        // 3. Hard fallback
        if (result.Count == 0)
        {
            var p = GetLampPrefab(FallbackLamp);
            if (p != null) result.Add(p);
        }

        return result;
    }

    private GameObject GetLampPrefab(string id)
    {
        switch (id?.Trim())
        {
            case "BlueLamp":      return BlueLampPrefab;
            case "GreenLamp":     return GreenLampPrefab;
            case "OceanBlueLamp": return OceanBlueLampPrefab;
            case "PinkLamp":      return PinkLampPrefab;
            case "RedLamp":       return RedLampPrefab;
            case "TechLamp":      return TechLampPrefab;
            case "YellowLamp":    return YellowLampPrefab;
            default:
                Debug.LogWarning($"[DecoSpawner] Unknown lamp id '{id}' — using RedLamp.");
                return RedLampPrefab;
        }
    }

    // ── Per-chamber spawn ─────────────────────────────────────────────────────

    private void SpawnInChamber(Transform chamber, Tilemap groundTm, List<GameObject> lampPrefabs)
    {
        var wallsTm = chamber.Find("Walls")?.GetComponent<Tilemap>();
        var validPositions = CollectValidPositions(groundTm, wallsTm);
        if (validPositions.Count == 0) return;

        // Lamps and decos each get their own shuffled copy so they can share tiles.
        var lampPositions = new List<Vector3>(validPositions);
        var decoPositions = new List<Vector3>(validPositions);
        Shuffle(lampPositions);
        Shuffle(decoPositions);

        // ── Lamps ──
        if (lampPrefabs.Count > 0)
        {
            int lampCount = Random.Range(LampsMin, LampsMax + 1);
            for (int i = 0; i < lampCount && i < lampPositions.Count; i++)
            {
                var prefab = lampPrefabs[i % lampPrefabs.Count];
                Instantiate(prefab, lampPositions[i], Quaternion.identity, chamber);
            }
        }

        // ── Decorations ──
        int decoTotal     = Random.Range(DecosMin, DecosMax + 1);
        int debuffCount   = Mathf.Min(Random.Range(0, DebuffingMax + 1), decoTotal);
        int cosmeticCount = decoTotal - debuffCount;

        var debuffPool   = BuildPool(AcidPoolPrefab, CobwebPrefab);
        var cosmeticPool = BuildPool(BarrelsPrefab, BonePilePrefab, WaterPoolPrefab, PotPrefab);

        Shuffle(debuffPool);
        Shuffle(cosmeticPool);

        int posIdx = 0;
        for (int i = 0; i < debuffCount && posIdx < decoPositions.Count; i++, posIdx++)
        {
            if (debuffPool.Count == 0) break;
            var prefab = debuffPool[i % debuffPool.Count];
            if (prefab != null)
                Instantiate(prefab, decoPositions[posIdx], Quaternion.identity, chamber);
        }

        for (int i = 0; i < cosmeticCount && posIdx < decoPositions.Count; i++, posIdx++)
        {
            if (cosmeticPool.Count == 0) break;
            var prefab = cosmeticPool[i % cosmeticPool.Count];
            if (prefab != null)
                Instantiate(prefab, decoPositions[posIdx], Quaternion.identity, chamber);
        }
    }

    // ── Position collection ───────────────────────────────────────────────────

    private List<Vector3> CollectValidPositions(Tilemap groundTm, Tilemap wallsTm)
    {
        var positions = new List<Vector3>();
        groundTm.CompressBounds();
        var bounds   = groundTm.cellBounds;
        int wallMask = LayerMask.GetMask("Walls");

        // Chambers are instantiated in the same frame as this call. Unity only syncs
        // new colliders to the physics engine on FixedUpdate, so OverlapCircle would
        // return null for all newly-spawned wall colliders without this call.
        Physics2D.SyncTransforms();

        foreach (var cellPos in bounds.allPositionsWithin)
        {
            if (!groundTm.HasTile(cellPos)) continue;
            if (IsExcluded(cellPos, bounds)) continue;
            // Fast tile-based reject
            if (wallsTm != null && wallsTm.HasTile(cellPos)) continue;

            Vector3 world = groundTm.CellToWorld(cellPos)
                          + new Vector3(groundTm.cellSize.x * 0.5f, groundTm.cellSize.y * 0.5f, 0f);
            world.z = 0f;

            // Physics-based reject — catches wall collider geometry that extends
            // beyond painted tiles (e.g. CompositeCollider2D margin, boundary walls)
            if (Physics2D.OverlapCircle(world, 0.4f, wallMask) != null) continue;

            positions.Add(world);
        }

        return positions;
    }

    /// <summary>
    /// Returns true if a cell should be excluded from placement (border margin or corridor opening).
    /// All coordinates are relative to the tilemap's cell bounds origin.
    /// </summary>
    private bool IsExcluded(Vector3Int pos, BoundsInt bounds)
    {
        int lx = pos.x - bounds.xMin;
        int ly = pos.y - bounds.yMin;
        int w  = bounds.size.x;
        int h  = bounds.size.y;

        // General border margin — avoid placing right at chamber edges
        if (lx < BorderMargin || ly < BorderMargin ||
            lx > w - 1 - BorderMargin || ly > h - 1 - BorderMargin)
            return true;

        // Bottom corridor opening zone
        if (ly < EdgeDepth && lx >= OpenMin && lx <= OpenMax) return true;
        // Top corridor opening zone
        if (ly > h - 1 - EdgeDepth && lx >= OpenMin && lx <= OpenMax) return true;
        // Left corridor opening zone
        if (lx < EdgeDepth && ly >= OpenMin && ly <= OpenMax) return true;
        // Right corridor opening zone
        if (lx > w - 1 - EdgeDepth && ly >= OpenMin && ly <= OpenMax) return true;

        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a pool from up to 4 nullable prefabs, duplicated once to allow repeats.</summary>
    private static List<GameObject> BuildPool(params GameObject[] prefabs)
    {
        var pool = new List<GameObject>();
        foreach (var p in prefabs)
            if (p != null) pool.Add(p);

        // Duplicate so modulo indexing works without bounds errors on small pools
        int original = pool.Count;
        for (int i = 0; i < original; i++)
            pool.Add(pool[i]);

        return pool;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
