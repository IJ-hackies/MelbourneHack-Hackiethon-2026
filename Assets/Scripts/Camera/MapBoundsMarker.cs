using Cinemachine;
using UnityEngine;

/// <summary>
/// Place on a GameObject named "MapBounds" inside each scene's Grid.
/// Position it at the centre of the playable area. CameraFollow reads this automatically.
/// Call SyncConfiner() after changing position or size to keep the Cinemachine confiner shape in sync.
/// </summary>
public class MapBoundsMarker : MonoBehaviour
{
    [Tooltip("Full width and height of the playable area in world units.")]
    public Vector2 size = new Vector2(40f, 22f);

    public Vector2 Center => (Vector2)transform.position;

    /// <summary>
    /// Creates or updates the PolygonCollider2D on this GameObject to match the current
    /// size, then tells the CinemachineConfiner to invalidate its baked path cache.
    /// Call this whenever position or size changes at runtime.
    /// </summary>
    public void SyncConfiner()
    {
        var poly = GetComponent<PolygonCollider2D>();
        if (poly == null)
        {
            poly           = gameObject.AddComponent<PolygonCollider2D>();
            poly.isTrigger = true;
        }

        float hw = size.x * 0.5f;
        float hh = size.y * 0.5f;
        poly.pathCount = 1;
        poly.SetPath(0, new Vector2[]
        {
            new Vector2(-hw, -hh),
            new Vector2( hw, -hh),
            new Vector2( hw,  hh),
            new Vector2(-hw,  hh),
        });

        FindAnyObjectByType<CinemachineConfiner>()?.InvalidatePathCache();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.DrawWireCube(transform.position, size);

        // Solid fill so it's visible even when not selected.
        Gizmos.color = new Color(0f, 1f, 1f, 0.04f);
        Gizmos.DrawCube(transform.position, size);
    }
}
