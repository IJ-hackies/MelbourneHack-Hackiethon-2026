using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

/// <summary>
/// Creates unified shadow casters for the entire floor by copying all wall tiles
/// into one tilemap, extracting merged CompositeCollider2D paths, and creating
/// one ShadowCaster2D per path (each only supports a single closed polygon).
///
/// Call BuildUnifiedShadow() after all chambers have been instantiated.
/// </summary>
public class UnifiedShadowCaster : MonoBehaviour
{
    private GameObject shadowObject;

    // Cached reflection fields
    private static FieldInfo s_shapePathField;
    private static FieldInfo s_hashField;
    private static FieldInfo s_castingSourceField;
    private static FieldInfo s_forceRebuildField;
    private static bool s_reflectionCached;

    public void BuildUnifiedShadow(Transform floorParent)
    {
        StartCoroutine(BuildUnifiedShadowRoutine(floorParent));
    }

    private IEnumerator BuildUnifiedShadowRoutine(Transform floorParent)
    {
        // Clean up any previous shadow object
        if (shadowObject != null)
        {
            Destroy(shadowObject);
            shadowObject = null;
        }

        // Wait a frame so all chamber colliders are fully initialized
        yield return null;

        // Collect all wall tilemaps from instantiated chambers
        var wallTilemaps = new List<Tilemap>();
        foreach (Transform chamber in floorParent)
        {
            var wallsTransform = chamber.Find("Walls");
            if (wallsTransform == null) continue;
            var tm = wallsTransform.GetComponent<Tilemap>();
            if (tm != null) wallTilemaps.Add(tm);
        }

        if (wallTilemaps.Count == 0)
        {
            Debug.LogWarning("[UnifiedShadowCaster] No wall tilemaps found.");
            yield break;
        }

        // Create shadow-only root — no Grid component, just a plain container
        shadowObject = new GameObject("UnifiedShadowCaster");
        shadowObject.transform.SetParent(floorParent);
        shadowObject.transform.position = floorParent.position;
        shadowObject.layer = LayerMask.NameToLayer("Walls");

        if (!CacheReflectionFields())
        {
            Debug.LogError("[UnifiedShadowCaster] Reflection failed — shadows disabled. " +
                           "Ensure Assets/link.xml preserves ShadowCaster2D.");
            yield break;
        }

        // Instead of copying tiles into a unified tilemap, read the composite
        // collider paths directly from each chamber's wall tilemap and create
        // shadow casters at the correct world positions.
        int totalPaths = 0;

        foreach (var wallTm in wallTilemaps)
        {
            var composite = wallTm.GetComponent<CompositeCollider2D>();
            if (composite == null) continue;

            // Force the composite to regenerate from the tilemap collider
            composite.GenerateGeometry();

            Transform wallTransform = wallTm.transform;

            for (int i = 0; i < composite.pathCount; i++)
            {
                var points = new Vector2[composite.GetPathPointCount(i)];
                composite.GetPath(i, points);

                if (points.Length < 3) continue;

                // Composite paths are in local space of the composite collider.
                // Create shadow caster at the same world position as the wall tilemap
                // so the path coordinates match.
                var pathGO = new GameObject($"Shadow_Path_{totalPaths}");
                pathGO.transform.SetParent(shadowObject.transform);
                pathGO.transform.position = wallTransform.position;
                pathGO.layer = LayerMask.NameToLayer("Walls");

                var caster = pathGO.AddComponent<ShadowCaster2D>();
                InjectPath(caster, points);
                totalPaths++;
            }
        }

        if (totalPaths > 500)
            Debug.LogWarning($"[UnifiedShadowCaster] High path count ({totalPaths}) — may impact performance.");

        Debug.Log($"[UnifiedShadowCaster] Built {totalPaths} shadow paths from {wallTilemaps.Count} wall tilemaps.");
    }

    private static bool CacheReflectionFields()
    {
        if (s_reflectionCached) return s_shapePathField != null;

        s_reflectionCached = true;
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var scType = typeof(ShadowCaster2D);

        s_shapePathField = scType.GetField("m_ShapePath", flags);
        s_hashField = scType.GetField("m_ShapePathHash", flags);
        s_castingSourceField = scType.GetField("m_ShadowCastingSource", flags);
        s_forceRebuildField = scType.GetField("m_ForceShadowMeshRebuild", flags);

        if (s_shapePathField == null)
        {
            Debug.LogError("[UnifiedShadowCaster] Could not find m_ShapePath on ShadowCaster2D — " +
                           "URP internal fields may have changed.");
            return false;
        }

        return true;
    }

    private static void InjectPath(ShadowCaster2D caster, Vector2[] path2D)
    {
        var path3D = new Vector3[path2D.Length];
        for (int i = 0; i < path2D.Length; i++)
            path3D[i] = new Vector3(path2D[i].x, path2D[i].y, 0f);

        s_shapePathField.SetValue(caster, path3D);

        // Set casting source to ShapeEditor (enum value 1) so it uses m_ShapePath
        if (s_castingSourceField != null)
            s_castingSourceField.SetValue(caster, 1);

        // Invalidate hash to force mesh rebuild
        if (s_hashField != null)
            s_hashField.SetValue(caster, Random.Range(int.MinValue, int.MaxValue));

        // Force shadow mesh rebuild
        if (s_forceRebuildField != null)
            s_forceRebuildField.SetValue(caster, true);

        caster.selfShadows = false;
    }

    private void OnDestroy()
    {
        if (shadowObject != null)
            Destroy(shadowObject);
    }
}
