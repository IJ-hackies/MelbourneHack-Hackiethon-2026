using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class StripShadowCasters
{
    [MenuItem("Tools/Strip ShadowCaster2D from Chamber Prefabs")]
    static void Strip()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Chambers" });
        int removed = 0;
        int prefabsModified = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            var casters = prefab.GetComponentsInChildren<ShadowCaster2D>(true);
            if (casters.Length == 0) continue;

            // Open prefab for editing
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

            var editCasters = root.GetComponentsInChildren<ShadowCaster2D>(true);
            foreach (var sc in editCasters)
            {
                Object.DestroyImmediate(sc);
                removed++;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            prefabsModified++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[StripShadowCasters] Done — removed {removed} ShadowCaster2D components from {prefabsModified} prefabs.");
    }
}
