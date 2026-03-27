using UnityEngine;
using UnityEngine.Events;

public class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;

    private float currentHealth;

    // Read
    public float Current  => currentHealth;
    public float Max      => maxHealth;
    public bool  IsDead   => currentHealth <= 0f;

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

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        currentHealth = Mathf.Max(0f, currentHealth - amount);
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
