using UnityEngine;

/// <summary>
/// Follows the player and clamps within map bounds.
/// Reads bounds from the scene's MapBoundsMarker automatically.
/// Falls back to the inspector fields if none is found.
/// Attach to the Main Camera.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Follow")]
    [SerializeField] private float smoothSpeed = 8f;

    [Header("Map Bounds (fallback if no MapBoundsMarker in scene)")]
    [SerializeField] private Vector2 mapCenter = Vector2.zero;
    [SerializeField] private Vector2 mapSize = new Vector2(40f, 22f);

    // Standard map size: 40×22 tiles at 1 world unit/tile (camera ortho size 5).
    public static readonly Vector2 StandardMapSize = new Vector2(40f, 22f);

    private Camera cam;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void Start()
    {
        var marker = FindObjectOfType<MapBoundsMarker>();
        if (marker != null)
        {
            mapCenter = marker.Center;
            mapSize   = marker.size;
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        float halfCamH = cam.orthographicSize;
        float halfCamW = halfCamH * cam.aspect;

        float minX = mapCenter.x - mapSize.x / 2f + halfCamW;
        float maxX = mapCenter.x + mapSize.x / 2f - halfCamW;
        float minY = mapCenter.y - mapSize.y / 2f + halfCamH;
        float maxY = mapCenter.y + mapSize.y / 2f - halfCamH;

        Vector3 desired = new Vector3(target.position.x, target.position.y, transform.position.z);
        desired.x = Mathf.Clamp(desired.x, minX, maxX);
        desired.y = Mathf.Clamp(desired.y, minY, maxY);

        transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(mapCenter, mapSize);
    }
}
