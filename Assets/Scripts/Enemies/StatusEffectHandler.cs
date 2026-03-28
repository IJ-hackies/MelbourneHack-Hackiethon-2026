using System.Collections;
using UnityEngine;

/// <summary>
/// Manages status effects on an enemy. Add to any enemy prefab (or auto-added at runtime).
/// Effects stack/refresh correctly and restore stats on expiry.
/// Visual feedback via SpriteRenderer tint — last applied effect wins.
/// </summary>
public class StatusEffectHandler : MonoBehaviour
{
    private EnemyBase enemy;
    private Health health;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;

    private Coroutine burnRoutine;
    private Coroutine freezeRoutine;
    private Coroutine slowRoutine;
    private Coroutine stunRoutine;
    private Coroutine poisonRoutine;

    private int poisonStacks = 0;
    private const int maxPoisonStacks = 3;

    private void Awake()
    {
        enemy = GetComponent<EnemyBase>();
        health = GetComponent<Health>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
            originalColor = spriteRenderer.color;
    }

    // --- Public API (called by ProjectileHandler) ---

    /// <summary>Deals 10% of damage every 0.5s for <paramref name="duration"/> seconds.</summary>
    public void ApplyBurn(float damage, float duration = 3f)
    {
        if (burnRoutine != null) StopCoroutine(burnRoutine);
        burnRoutine = StartCoroutine(BurnRoutine(damage, duration));
    }

    /// <summary>Stops movement and triples attack cooldown for <paramref name="duration"/> seconds.</summary>
    public void ApplyFreeze(float duration = 2f)
    {
        if (freezeRoutine != null) StopCoroutine(freezeRoutine);
        // Cancel weaker effects — freeze supersedes slow and stun
        if (slowRoutine != null) { StopCoroutine(slowRoutine); slowRoutine = null; }
        if (stunRoutine != null) { StopCoroutine(stunRoutine); stunRoutine = null; }
        freezeRoutine = StartCoroutine(FreezeRoutine(duration));
    }

    /// <summary>Reduces move speed to 40% for <paramref name="duration"/> seconds.</summary>
    public void ApplySlow(float duration = 2f)
    {
        if (freezeRoutine != null || stunRoutine != null) return; // superseded
        if (slowRoutine != null) StopCoroutine(slowRoutine);
        slowRoutine = StartCoroutine(SlowRoutine(duration));
    }

    /// <summary>Stops movement and disables attacking for <paramref name="duration"/> seconds.</summary>
    public void ApplyStun(float duration = 1.5f)
    {
        if (freezeRoutine != null) return; // freeze supersedes
        if (stunRoutine != null) StopCoroutine(stunRoutine);
        if (slowRoutine != null) { StopCoroutine(slowRoutine); slowRoutine = null; }
        stunRoutine = StartCoroutine(StunRoutine(duration));
    }

    /// <summary>Deals 5% of damage per stack per second for <paramref name="duration"/> seconds. Stacks up to 3x.</summary>
    public void ApplyPoison(float damage, float duration = 5f)
    {
        poisonStacks = Mathf.Min(poisonStacks + 1, maxPoisonStacks);
        if (poisonRoutine != null) StopCoroutine(poisonRoutine);
        poisonRoutine = StartCoroutine(PoisonRoutine(damage, duration));
    }

    // --- Coroutines ---

    private IEnumerator BurnRoutine(float damage, float duration)
    {
        SetTint(new Color(1f, 0.4f, 0.1f)); // orange
        float elapsed = 0f;
        const float tickInterval = 0.5f;
        float tickDamage = damage * 0.1f;

        while (elapsed < duration)
        {
            yield return new WaitForSeconds(tickInterval);
            elapsed += tickInterval;
            if (health.IsDead) break;
            health.TakeDamage(tickDamage);
        }

        burnRoutine = null;
        RefreshTint();
    }

    private IEnumerator FreezeRoutine(float duration)
    {
        SetTint(new Color(0.5f, 0.85f, 1f)); // icy blue
        float savedSpeed = enemy.MoveSpeed;
        float savedCooldown = enemy.AttackCooldown;
        enemy.MoveSpeed = 0f;
        enemy.AttackCooldown = savedCooldown * 3f;

        yield return new WaitForSeconds(duration);

        if (!health.IsDead)
        {
            enemy.MoveSpeed = savedSpeed;
            enemy.AttackCooldown = savedCooldown;
        }
        freezeRoutine = null;
        RefreshTint();
    }

    private IEnumerator SlowRoutine(float duration)
    {
        SetTint(new Color(0.6f, 0.7f, 1f)); // pale blue
        float savedSpeed = enemy.MoveSpeed;
        enemy.MoveSpeed = savedSpeed * 0.4f;

        yield return new WaitForSeconds(duration);

        if (!health.IsDead)
            enemy.MoveSpeed = savedSpeed;
        slowRoutine = null;
        RefreshTint();
    }

    private IEnumerator StunRoutine(float duration)
    {
        SetTint(new Color(1f, 1f, 0.3f)); // yellow
        float savedSpeed = enemy.MoveSpeed;
        float savedCooldown = enemy.AttackCooldown;
        enemy.MoveSpeed = 0f;
        enemy.AttackCooldown = float.MaxValue;

        yield return new WaitForSeconds(duration);

        if (!health.IsDead)
        {
            enemy.MoveSpeed = savedSpeed;
            enemy.AttackCooldown = savedCooldown;
        }
        stunRoutine = null;
        RefreshTint();
    }

    private IEnumerator PoisonRoutine(float damage, float duration)
    {
        SetTint(new Color(0.4f, 1f, 0.3f)); // green
        float elapsed = 0f;

        while (elapsed < duration)
        {
            yield return new WaitForSeconds(1f);
            elapsed += 1f;
            if (health.IsDead) break;
            health.TakeDamage(damage * 0.05f * poisonStacks);
        }

        poisonStacks = 0;
        poisonRoutine = null;
        RefreshTint();
    }

    // --- Tint helpers ---

    private void SetTint(Color color)
    {
        if (spriteRenderer != null)
            spriteRenderer.color = color;
    }

    /// <summary>Restores original color only when all effects have expired.</summary>
    private void RefreshTint()
    {
        if (burnRoutine != null || freezeRoutine != null ||
            slowRoutine != null || stunRoutine != null || poisonRoutine != null)
            return;

        if (spriteRenderer != null)
            spriteRenderer.color = originalColor;
    }
}
