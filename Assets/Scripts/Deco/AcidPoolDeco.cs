using System.Collections;
using UnityEngine;

/// <summary>
/// AcidPool decoration — poisons the player for 3 s on contact.
/// Per-pool cooldown of 3 s prevents spam re-application.
/// Requires a trigger Collider2D on this GameObject.
/// A kinematic Rigidbody2D is added at runtime if absent (needed for trigger events).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class AcidPoolDeco : MonoBehaviour
{
    private const float PoisonDuration = 3f;
    private const float Cooldown       = 3f;

    private float cooldownTimer = 0f;

    private void Awake()
    {
        // Ensure trigger is set
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;

        // Need a Rigidbody2D for trigger callbacks to fire against other triggers
        if (GetComponent<Rigidbody2D>() == null)
        {
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType    = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
        }
    }

    private void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (cooldownTimer > 0f) return;
        if (!other.CompareTag("Player")) return;

        var pse = other.GetComponent<PlayerStatusEffects>();
        if (pse == null) return;

        pse.ApplyPoison(PoisonDuration);
        cooldownTimer = Cooldown;
    }
}
