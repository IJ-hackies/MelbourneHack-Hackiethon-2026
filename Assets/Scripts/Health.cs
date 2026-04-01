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
    public UnityEvent<float> OnHealed;
    public UnityEvent OnDeath;

    private void Awake()
    {
        currentHealth = maxHealth;
        // Ensure events are never null (OnHealed won't be serialized on old prefabs)
        if (OnDamaged == null) OnDamaged = new UnityEvent<float>();
        if (OnHealed == null)  OnHealed  = new UnityEvent<float>();
        if (OnDeath == null)   OnDeath   = new UnityEvent();
    }

    // Scaling: set a new max and optionally rescale current HP proportionally
    public void SetMaxHealth(float newMax, bool rescaleCurrent = true)
    {
        float ratio = rescaleCurrent ? (currentHealth / maxHealth) : 1f;
        maxHealth = Mathf.Max(1f, newMax);
        currentHealth = rescaleCurrent ? maxHealth * ratio : maxHealth;
    }

    public bool IsInvulnerable { get; set; }

    // Set by ProjectileHandler before calling TakeDamage; read by DamageNumberSpawner.
    public bool LastHitWasCrit { get; set; }

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
        float before = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        float actual = currentHealth - before;
        if (actual > 0f)
            OnHealed?.Invoke(actual);
    }
}
