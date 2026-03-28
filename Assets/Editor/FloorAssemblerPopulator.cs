using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility that auto-populates FloorAssembler.tilesetLibraries by scanning
/// Assets/Chambers/ for tileset folders and loading every prefab inside each Prefabs/ subfolder.
///
/// Usage: select the GameObject with FloorAssembler in the scene, then
/// Tools > Populate Chamber Library.
/// </summary>
public static class FloorAssemblerPopulator
{
    private const string ChambersRoot = "Assets/Chambers";

    [MenuItem("Tools/Populate Chamber Library")]
    static void Populate()
    {
        var assembler = Object.FindObjectOfType<FloorAssembler>();
        if (assembler == null)
        {
            Debug.LogError("FloorAssemblerPopulator: no FloorAssembler found in the open scene.");
            return;
        }

        if (!Directory.Exists(ChambersRoot))
        {
            Debug.LogError($"FloorAssemblerPopulator: '{ChambersRoot}' directory not found.");
            return;
        }

        Undo.RecordObject(assembler, "Populate Chamber Library");
        assembler.tilesetLibraries.Clear();

        int totalChambers = 0;

        foreach (string tilesetDir in Directory.GetDirectories(ChambersRoot))
        {
            string tilesetId  = Path.GetFileName(tilesetDir);
            string prefabsDir = Path.Combine(tilesetDir, "Prefabs").Replace('\\', '/');

            if (!Directory.Exists(prefabsDir)) continue;

            var tilesetEntry = new FloorAssembler.TilesetLibrary { tilesetId = tilesetId };

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabsDir });
            foreach (string guid in guids)
            {
                string path      = AssetDatabase.GUIDToAssetPath(guid);
                string chamberId = Path.GetFileNameWithoutExtension(path);
                var    prefab    = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                tilesetEntry.chambers.Add(new FloorAssembler.ChamberEntry
                {
                    id     = chamberId,
                    prefab = prefab,
                });
            }

            if (tilesetEntry.chambers.Count == 0) continue;

            assembler.tilesetLibraries.Add(tilesetEntry);
            totalChambers += tilesetEntry.chambers.Count;
            Debug.Log($"  [{tilesetId}] — {tilesetEntry.chambers.Count} chambers");
        }

        EditorUtility.SetDirty(assembler);
        Debug.Log($"FloorAssemblerPopulator: populated {assembler.tilesetLibraries.Count} tilesets, {totalChambers} chambers total.");
    }

    [MenuItem("Tools/Populate Chamber Library", validate = true)]
    static bool ValidatePopulate()
    {
        return Object.FindObjectOfType<FloorAssembler>() != null;
    }
}
