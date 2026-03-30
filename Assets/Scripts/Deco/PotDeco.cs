using System.Collections;
using UnityEngine;

/// <summary>
/// Pot decoration — when hit by a player spell projectile, swaps to BrokenPot
/// and destroys itself after a short delay. Never reacts to the player walking into it.
/// Requires a trigger Collider2D on this GameObject.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class PotDeco : MonoBehaviour
{
    [SerializeField] private GameObject brokenPotPrefab;
    [SerializeField] private float      disappearDelay = 0.4f;

    private bool broken;

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
        if (broken) return;
        if (other.GetComponent<ProjectileHandler>() == null) return;

        broken = true;

        if (brokenPotPrefab != null)
            Instantiate(brokenPotPrefab, transform.position, transform.rotation, transform.parent);

        Destroy(gameObject, disappearDelay);
    }
}
