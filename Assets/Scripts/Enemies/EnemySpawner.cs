using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns enemies from the Floor Manifest at the 12 exterior map entry points.
///
/// Entry points are the openings on the outer edges of the 4x2 chamber grid.
/// All openings are centred at tile 9.5 of each 20-unit chamber edge.
/// Spawn order and entry point selection are fully randomised.
///
/// Setup:
/// 1. Add this component to a scene GameObject (or the same one as FloorAssembler).
/// 2. Populate Enemy Library with all enemy prefabs.
/// 3. FloorAssembler calls SpawnFloor() after AssembleFloor().
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [System.Serializable]
    public class EnemyEntry
    {
        public string     id;
        public GameObject prefab;
    }

    [Header("Enemy Library — wire up all enemy prefabs here")]
    public List<EnemyEntry> enemyLibrary = new List<EnemyEntry>();

    [Header("Timing")]
    [Tooltip("Seconds between each enemy being spawned.")]
    public float spawnInterval = 0.5f;

    // Chamber grid constants — must match FloorAssembler
    private const int   ChamberSize  = 20;
    private const float OpeningCenter = 9.5f;  // tile 9.5 in local chamber space
    private const float SpawnInset   = 2f;     // how far INSIDE the map edge to place the enemy

    // 12 exterior entry points computed from the 4x2 grid geometry.
    // Enemies are placed just inside the map edge so boundary wall colliders
    // don't block them from entering.
    // Assumes the assembler's origin is (0,0). If FloorAssembler is offset,
    // pass its position into SpawnFloor().
    private static Vector2[] ComputeEntryPoints(Vector2 assemblerOrigin)
    {
        float left   = assemblerOrigin.x + SpawnInset;
        float right  = assemblerOrigin.x + ChamberSize * 4 - SpawnInset;
        float bottom = assemblerOrigin.y + SpawnInset;
        float top    = assemblerOrigin.y + ChamberSize * 2 - SpawnInset;

        // Opening X positions for bottom/top (one per column)
        float[] colX = new float[4];
        for (int col = 0; col < 4; col++)
            colX[col] = assemblerOrigin.x + col * ChamberSize + OpeningCenter;

        // Opening Y positions for left/right (one per row)
        float[] rowY = new float[2];
        for (int row = 0; row < 2; row++)
            rowY[row] = assemblerOrigin.y + row * ChamberSize + OpeningCenter;

        return new Vector2[]
        {
            // Bottom edge — 4 openings
            new Vector2(colX[0], bottom),
            new Vector2(colX[1], bottom),
            new Vector2(colX[2], bottom),
            new Vector2(colX[3], bottom),

            // Top edge — 4 openings
            new Vector2(colX[0], top),
            new Vector2(colX[1], top),
            new Vector2(colX[2], top),
            new Vector2(colX[3], top),

            // Left edge — 2 openings
            new Vector2(left, rowY[0]),
            new Vector2(left, rowY[1]),

            // Right edge — 2 openings
            new Vector2(right, rowY[0]),
            new Vector2(right, rowY[1]),
        };
    }

    /// <summary>
    /// Called by FloorAssembler after the map is assembled.
    /// </summary>
    public void SpawnFloor(List<EnemySpawnDTO> spawns, Vector2 assemblerOrigin)
    {
        StopAllCoroutines();
        StartCoroutine(SpawnSequence(spawns, assemblerOrigin));
    }

    private IEnumerator SpawnSequence(List<EnemySpawnDTO> spawns, Vector2 assemblerOrigin)
    {
        var library      = BuildLibrary();
        var entryPoints  = ComputeEntryPoints(assemblerOrigin);
        var queue        = BuildShuffledQueue(spawns, library);

        int   stage       = StageDirector.Instance != null ? StageDirector.Instance.StageNumber : 1;
        // HP grows by 22% per stage (stage 1 = 1.0×, stage 5 = 1.88×, stage 10 = 2.98×)
        float healthScale = 1f + (stage - 1) * 0.22f;

        foreach (var (prefab, modifiers) in queue)
        {
            Vector2 spawnPos = entryPoints[Random.Range(0, entryPoints.Length)];
            var go = Instantiate(prefab, spawnPos, Quaternion.identity);

            // Scale enemy HP for the current stage before applying modifiers
            if (stage > 1)
            {
                var hp = go.GetComponent<Health>();
                if (hp != null) hp.SetMaxHealth(hp.Max * healthScale);
            }

            ApplyModifiers(go, modifiers);
            FloorClearDetector.Instance?.RegisterEnemy(go);
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    // Expands spawn entries into a flat list (e.g. 3x melee_charger → 3 items) then shuffles.
    private List<(GameObject prefab, string[] modifiers)> BuildShuffledQueue(
        List<EnemySpawnDTO> spawns,
        Dictionary<string, GameObject> library)
    {
        var flat = new List<(GameObject, string[])>();

        foreach (var entry in spawns)
        {
            if (!library.TryGetValue(entry.enemy_id, out GameObject prefab))
            {
                Debug.LogWarning($"EnemySpawner: enemy id '{entry.enemy_id}' not found in library — skipping.");
                continue;
            }

            int count = Mathf.Max(0, entry.count);
            for (int i = 0; i < count; i++)
                flat.Add((prefab, entry.modifiers ?? System.Array.Empty<string>()));
        }

        // Fisher-Yates shuffle
        for (int i = flat.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (flat[i], flat[j]) = (flat[j], flat[i]);
        }

        return flat;
    }

    private void ApplyModifiers(GameObject enemy, string[] modifiers)
    {
        if (modifiers == null || modifiers.Length == 0) return;

        var enemyBase = enemy.GetComponent<EnemyBase>();
        var health    = enemy.GetComponent<Health>();

        foreach (string mod in modifiers)
        {
            switch (mod)
            {
                case "armored":
                    // 50% damage reduction via DamageReduction on Health
                    if (health != null) health.DamageReduction = 0.5f;
                    break;

                case "berserk":
                    // Double speed and damage, half HP
                    if (enemyBase != null)
                    {
                        enemyBase.MoveSpeed    *= 2f;
                        enemyBase.AttackDamage *= 2f;
                    }
                    if (health != null) health.SetMaxHealth(health.Max * 0.5f);
                    break;

                case "regenerating":
                    enemy.AddComponent<EnemyRegen>();
                    break;

                default:
                    // elemental_immune:fire etc. — log for now, implement when status system is ready
                    Debug.Log($"EnemySpawner: modifier '{mod}' not yet handled.");
                    break;
            }
        }
    }

    private Dictionary<string, GameObject> BuildLibrary()
    {
        var lib = new Dictionary<string, GameObject>();
        foreach (var entry in enemyLibrary)
            if (entry.prefab != null && !string.IsNullOrEmpty(entry.id))
                lib[entry.id] = entry.prefab;
        return lib;
    }
}
