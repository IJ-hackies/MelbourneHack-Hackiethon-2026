using UnityEngine;
using TMPro;

/// <summary>
/// Auto-attaches to any GameObject with a Health component.
/// Listens to OnDamaged/OnHealed and spawns FloatingDamageNumber instances.
/// </summary>
[RequireComponent(typeof(Health))]
public class DamageNumberSpawner : MonoBehaviour
{
    public static TMP_FontAsset SharedFont { get; set; }

    private Health health;
    private bool listening;

    private void Start()
    {
        // Use Start instead of Awake/OnEnable to guarantee Health.Awake()
        // has initialized the events (especially OnHealed on old prefabs).
        health = GetComponent<Health>();
        Subscribe();
    }

    private void OnEnable()
    {
        if (health != null && !listening)
            Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (health == null || listening) return;
        health.OnDamaged?.AddListener(OnDamaged);
        health.OnHealed?.AddListener(OnHealed);
        listening = true;
    }

    private void Unsubscribe()
    {
        if (health == null || !listening) return;
        health.OnDamaged?.RemoveListener(OnDamaged);
        health.OnHealed?.RemoveListener(OnHealed);
        listening = false;
    }

    private void OnDamaged(float amount)
    {
        if (amount <= 0f) return;
        bool isCrit = health.LastHitWasCrit;
        health.LastHitWasCrit = false;
        Debug.Log($"[DmgNum] Damage {amount}{(isCrit ? " CRIT" : "")} on {gameObject.name}");
        FloatingDamageNumber.Spawn(transform.position, amount, false, SharedFont, isCrit);
    }

    private void OnHealed(float amount)
    {
        if (amount <= 0f) return;
        Debug.Log($"[DmgNum] Heal {amount} on {gameObject.name}");
        FloatingDamageNumber.Spawn(transform.position, amount, true, SharedFont);
    }
}
