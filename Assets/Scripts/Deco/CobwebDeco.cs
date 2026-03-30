using UnityEngine;

/// <summary>
/// Cobweb decoration — silently reduces player speed to 50% while in contact.
/// No status effect icon or visual is shown; movement is simply halved.
/// On exit the saved multiplier is restored.
/// Requires a trigger Collider2D on this GameObject.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CobwebDeco : MonoBehaviour
{
    private const float SlowMultiplier = 0.5f;

    private PlayerMovement trackedMovement;
    private float          savedMultiplier = 1f;

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;

        if (GetComponent<Rigidbody2D>() == null)
        {
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType    = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (trackedMovement != null) return; // already slowing

        var movement = other.GetComponent<PlayerMovement>();
        if (movement == null) return;

        trackedMovement = movement;
        savedMultiplier = movement.SpeedMultiplier;
        movement.SpeedMultiplier = savedMultiplier * SlowMultiplier;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (trackedMovement == null) return;

        trackedMovement.SpeedMultiplier = savedMultiplier;
        trackedMovement = null;
    }

    private void OnDestroy()
    {
        // Restore speed if cobweb is destroyed while player is inside
        if (trackedMovement != null)
        {
            trackedMovement.SpeedMultiplier = savedMultiplier;
            trackedMovement = null;
        }
    }
}
