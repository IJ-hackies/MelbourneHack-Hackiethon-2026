using UnityEngine;

/// <summary>
/// Plays SFX tied to the player. Add this component to the Player GameObject.
/// Subscribes to the player's Health events — no modifications to Health.cs needed.
/// </summary>
[RequireComponent(typeof(Health))]
public class PlayerSFX : MonoBehaviour
{
    private Health health;

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        health.OnDamaged.AddListener(OnDamaged);
    }

    private void OnDisable()
    {
        health.OnDamaged.RemoveListener(OnDamaged);
    }

    private void OnDamaged(float amount)
    {
        SFXManager.Instance?.PlayPlayerHit();
    }
}
