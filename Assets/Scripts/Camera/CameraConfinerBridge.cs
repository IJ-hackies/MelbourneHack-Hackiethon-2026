using Cinemachine;
using UnityEngine;

/// <summary>
/// Attach to the Cinemachine Virtual Camera GameObject.
/// At Start, finds the MapBoundsMarker, ensures its PolygonCollider2D is built,
/// then wires a CinemachineConfiner extension and applies the dead zone — no
/// manual inspector dragging required.
/// </summary>
[RequireComponent(typeof(CinemachineVirtualCamera))]
public class CameraConfinerBridge : MonoBehaviour
{
    [Header("Confiner")]
    [Tooltip("0 = hard stop at edge. 0.5 = soft ease into boundary.")]
    [SerializeField] private float confineDamping = 0.5f;

    [Header("Dead Zone (fraction of screen, 0–1)")]
    [SerializeField] private float deadZoneWidth  = 0.15f;
    [SerializeField] private float deadZoneHeight = 0.10f;

    private void Start()
    {
        var marker = FindAnyObjectByType<MapBoundsMarker>();
        if (marker == null)
        {
            Debug.LogWarning("CameraConfinerBridge: no MapBoundsMarker found in scene — confiner not applied.");
            return;
        }

        // Build / refresh the PolygonCollider2D on the MapBounds object.
        marker.SyncConfiner();

        // Add confiner extension if not already present, then wire it.
        var confiner = GetComponent<CinemachineConfiner>();
        if (confiner == null)
            confiner = gameObject.AddComponent<CinemachineConfiner>();

        confiner.m_ConfineMode        = CinemachineConfiner.Mode.Confine2D;
        confiner.m_ConfineScreenEdges = true;
        confiner.m_BoundingShape2D    = marker.GetComponent<PolygonCollider2D>();
        confiner.m_Damping            = confineDamping;
        confiner.InvalidatePathCache();

        // Apply dead zone to the Framing Transposer body.
        var vcam       = GetComponent<CinemachineVirtualCamera>();
        var transposer = vcam.GetCinemachineComponent<CinemachineFramingTransposer>();
        if (transposer != null)
        {
            transposer.m_DeadZoneWidth  = deadZoneWidth;
            transposer.m_DeadZoneHeight = deadZoneHeight;
        }
    }
}
