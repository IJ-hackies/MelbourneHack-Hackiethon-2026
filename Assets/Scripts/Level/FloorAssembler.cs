using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Assembles a floor from 8 chamber prefabs arranged in a 4x2 grid (80x40 world units).
///
/// Setup:
/// 1. Add this component to an empty GameObject in your scene.
/// 2. Populate Chamber Library with all chamber prefabs (drag from Assets/Chambers/<tileset>/Prefabs/).
/// 3. Fill Chamber Grid with 8 chamber IDs (index 0-3 = bottom row left-to-right, 4-7 = top row).
/// 4. Hit Play — or right-click the component > Assemble Floor (Editor Preview).
/// </summary>
public class FloorAssembler : MonoBehaviour
{
    [System.Serializable]
    public class ChamberEntry
    {
        public string     id;
        public GameObject prefab;
    }

    [Header("Chamber Library — add all prefabs from Assets/Chambers/<tileset>/Prefabs/")]
    public List<ChamberEntry> chamberLibrary = new List<ChamberEntry>();

    [Header("Grid — 8 chamber IDs, row-major (index 0-3 = bottom row, 4-7 = top row)")]
    [Tooltip(
        "Visual layout:\n" +
        "[4][5][6][7]  ← top row    (y = 20)\n" +
        "[0][1][2][3]  ← bottom row (y =  0)")]
    public string[] chamberGrid = new string[8];

    private const int ChamberSize = 20;  // world units per chamber (matches 20x20 tilemap)

    private void Start()
    {
        AssembleFloor();
    }

    public void AssembleFloor()
    {
        // Clear any previously assembled chambers
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        var library = BuildLibrary();

        for (int i = 0; i < 8; i++)
        {
            if (i >= chamberGrid.Length) break;
            string id = chamberGrid[i];
            if (string.IsNullOrEmpty(id)) continue;

            if (!library.TryGetValue(id, out GameObject prefab))
            {
                Debug.LogWarning($"FloorAssembler: chamber '{id}' not found in library — skipping.");
                continue;
            }

            int col = i % 4;
            int row = i / 4;
            Vector3 worldOffset = transform.position + new Vector3(col * ChamberSize, row * ChamberSize, 0f);

            Instantiate(prefab, worldOffset, Quaternion.identity, transform);
        }

        PositionMapBounds();
        SpawnBoundaryWalls();
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

    private Dictionary<string, GameObject> BuildLibrary()
    {
        var lib = new Dictionary<string, GameObject>();
        foreach (var entry in chamberLibrary)
            if (entry.prefab != null && !string.IsNullOrEmpty(entry.id))
                lib[entry.id] = entry.prefab;
        return lib;
    }

    private void PositionMapBounds()
    {
        var marker = FindObjectOfType<MapBoundsMarker>();
        if (marker == null) return;

        // Centre of the 80x40 grid, relative to this assembler's position
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
        // Use DestroyImmediate in edit mode
        for (int i = transform.childCount - 1; i >= 0; i--)
            UnityEditor.Undo.DestroyObjectImmediate(transform.GetChild(i).gameObject);

        var library = BuildLibrary();

        for (int i = 0; i < 8; i++)
        {
            if (i >= chamberGrid.Length) break;
            string id = chamberGrid[i];
            if (string.IsNullOrEmpty(id)) continue;

            if (!library.TryGetValue(id, out GameObject prefab))
            {
                Debug.LogWarning($"FloorAssembler: chamber '{id}' not found — skipping.");
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
