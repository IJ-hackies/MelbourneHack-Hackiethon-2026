using UnityEngine;

/// <summary>
/// Place on a GameObject named "MapBounds" inside each scene's Grid.
/// Position it at the centre of the playable area. CameraFollow reads this automatically.
/// </summary>
public class MapBoundsMarker : MonoBehaviour
{
    [Tooltip("Full width and height of the playable area in world units.")]
    public Vector2 size = new Vector2(40f, 22f);

    public Vector2 Center => (Vector2)transform.position;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.DrawWireCube(transform.position, size);

        // Solid fill so it's visible even when not selected.
        Gizmos.color = new Color(0f, 1f, 1f, 0.04f);
        Gizmos.DrawCube(transform.position, size);
    }
}
