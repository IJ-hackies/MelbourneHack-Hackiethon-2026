using UnityEngine;
using UnityEngine.Events;

public class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;

    private float currentHealth;

    // Read
    public float Current         => currentHealth;
    public float Max             => maxHealth;
    public bool  IsDead          => currentHealth <= 0f;

    // Set by status effects (e.g. Poison vulnerability). Values > 1 increase damage taken.
    public float DamageMultiplier { get; set; } = 1f;

    // Set by EnemySpawner modifiers (e.g. "armored"). 0 = no reduction, 0.5 = 50% reduction.
    [HideInInspector] public float DamageReduction = 0f;

    public UnityEvent<float> OnDamaged;
    public UnityEvent OnDeath;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    // Scaling: set a new max and optionally rescale current HP proportionally
    public void SetMaxHealth(float newMax, bool rescaleCurrent = true)
    {
        float ratio = rescaleCurrent ? (currentHealth / maxHealth) : 1f;
        maxHealth = Mathf.Max(1f, newMax);
        currentHealth = rescaleCurrent ? maxHealth * ratio : maxHealth;
    }

    public bool IsInvulnerable { get; set; }

    public void TakeDamage(float amount)
    {
        if (IsDead || IsInvulnerable) return;
        currentHealth = Mathf.Max(0f, currentHealth - amount * DamageMultiplier);
        OnDamaged.Invoke(amount);
        if (IsDead)
            OnDeath.Invoke();
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }
}
