using System.Collections;
using UnityEngine;

/// <summary>
/// Manages status effects on an enemy. Auto-added at runtime if missing.
/// All effects stack/refresh correctly and restore stats on expiry.
/// Visual feedback via SpriteRenderer tint — last applied effect wins on tint,
/// but underlying stacks are tracked independently.
/// </summary>
public class StatusEffectHandler : MonoBehaviour
{
    private EnemyBase      enemy;
    private Health         health;
    private Rigidbody2D    enemyRb;
    private SpriteRenderer spriteRenderer;
    private Color          originalColor;
    private Transform      playerTransform;

    // ── Coroutine handles ─────────────────────────────────────────────────────
    private Coroutine burnRoutine;
    private Coroutine freezeRoutine;
    private Coroutine slowRoutine;
    private Coroutine stunRoutine;
    private Coroutine poisonRoutine;
    private Coroutine bleedRoutine;
    private Coroutine rootRoutine;
    private Coroutine weaknessRoutine;
    private Coroutine curseRoutine;
    private Coroutine blindRoutine;

    private int   poisonStacks   = 0;
    private float curseSpeed     = 0f;
    private bool  isCursed       = false;

    private const int MaxPoisonStacks = 3;

    private void Awake()
    {
        enemy          = GetComponent<EnemyBase>();
        health         = GetComponent<Health>();
        enemyRb        = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null) originalColor = spriteRenderer.color;

        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) playerTransform = playerObj.transform;
    }

    // ── CURSE movement override ───────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (!isCursed || playerTransform == null || health == null || health.IsDead) return;
        if (enemyRb == null) return;

        Vector2 awayFromPlayer = ((Vector2)transform.position - (Vector2)playerTransform.position).normalized;
        enemyRb.MovePosition(enemyRb.position + awayFromPlayer * curseSpeed * Time.fixedDeltaTime);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void ApplyBurn(float damage, float duration = 3f)
    {
        if (burnRoutine != null) StopCoroutine(burnRoutine);
        burnRoutine = StartCoroutine(BurnRoutine(damage, duration));
    }

    public void ApplyFreeze(float duration = 2f)
    {
        if (freezeRoutine != null) StopCoroutine(freezeRoutine);
        if (slowRoutine  != null) { StopCoroutine(slowRoutine);  slowRoutine  = null; }
        if (stunRoutine  != null) { StopCoroutine(stunRoutine);  stunRoutine  = null; }
        freezeRoutine = StartCoroutine(FreezeRoutine(duration));
    }

    public void ApplySlow(float duration = 2f)
    {
        if (freezeRoutine != null || stunRoutine != null) return; // superseded
        if (slowRoutine   != null) StopCoroutine(slowRoutine);
        slowRoutine = StartCoroutine(SlowRoutine(duration));
    }

    public void ApplyStun(float duration = 1.5f)
    {
        if (freezeRoutine != null) return;
        if (stunRoutine   != null) StopCoroutine(stunRoutine);
        if (slowRoutine   != null) { StopCoroutine(slowRoutine); slowRoutine = null; }
        stunRoutine = StartCoroutine(StunRoutine(duration));
    }

    public void ApplyPoison(float damage, float duration = 5f)
    {
        poisonStacks = Mathf.Min(poisonStacks + 1, MaxPoisonStacks);
        if (poisonRoutine != null) StopCoroutine(poisonRoutine);
        poisonRoutine = StartCoroutine(PoisonRoutine(damage, duration));
    }

    /// <summary>BLEED — escalating DoT: each tick deals 5% more than the previous.</summary>
    public void ApplyBleed(float damage, float duration = 4f)
    {
        if (bleedRoutine != null) StopCoroutine(bleedRoutine);
        bleedRoutine = StartCoroutine(BleedRoutine(damage, duration));
    }

    /// <summary>ROOT — freezes position for 2s; enemy can still attack.</summary>
    public void ApplyRoot(float duration = 2f)
    {
        if (freezeRoutine != null || stunRoutine != null) return; // stronger effects win
        if (rootRoutine   != null) StopCoroutine(rootRoutine);
        rootRoutine = StartCoroutine(RootRoutine(duration));
    }

    /// <summary>WEAKNESS — multiplies all incoming damage on this enemy by 1.5× for 3s.</summary>
    public void ApplyWeakness(float duration = 3f)
    {
        if (weaknessRoutine != null) StopCoroutine(weaknessRoutine);
        weaknessRoutine = StartCoroutine(WeaknessRoutine(duration));
    }

    /// <summary>CURSE — enemy flees from player for 3s (movement inverted).</summary>
    public void ApplyCurse(float duration = 3f)
    {
        if (curseRoutine != null) StopCoroutine(curseRoutine);
        curseRoutine = StartCoroutine(CurseRoutine(duration));
    }

    /// <summary>BLIND — enemy loses player detection for 2.5s and wanders randomly.</summary>
    public void ApplyBlind(float duration = 2.5f)
    {
        if (blindRoutine != null) StopCoroutine(blindRoutine);
        blindRoutine = StartCoroutine(BlindRoutine(duration));
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    private IEnumerator BurnRoutine(float damage, float duration)
    {
        SetTint(new Color(1f, 0.4f, 0.1f));
        float elapsed   = 0f;
        float tickDmg   = damage * 0.1f;
        const float Interval = 0.5f;

        while (elapsed < duration)
        {
            yield return new WaitForSeconds(Interval);
            elapsed += Interval;
            if (health.IsDead) break;
            health.TakeDamage(tickDmg);
        }
        burnRoutine = null;
        RefreshTint();
    }

    private IEnumerator FreezeRoutine(float duration)
    {
        SetTint(new Color(0.5f, 0.85f, 1f));
        float savedSpeed    = enemy != null ? enemy.MoveSpeed    : 0f;
        float savedCooldown = enemy != null ? enemy.AttackCooldown : 1f;
        if (enemy != null) { enemy.MoveSpeed = 0f; enemy.AttackCooldown = savedCooldown * 3f; }

        yield return new WaitForSeconds(duration);

        if (!health.IsDead && enemy != null)
        { enemy.MoveSpeed = savedSpeed; enemy.AttackCooldown = savedCooldown; }
        freezeRoutine = null;
        RefreshTint();
    }

    private IEnumerator SlowRoutine(float duration)
    {
        SetTint(new Color(0.6f, 0.7f, 1f));
        float savedSpeed = enemy != null ? enemy.MoveSpeed : 0f;
        if (enemy != null) enemy.MoveSpeed = savedSpeed * 0.4f;

        yield return new WaitForSeconds(duration);

        if (!health.IsDead && enemy != null) enemy.MoveSpeed = savedSpeed;
        slowRoutine = null;
        RefreshTint();
    }

    private IEnumerator StunRoutine(float duration)
    {
        SetTint(new Color(1f, 1f, 0.3f));
        float savedSpeed    = enemy != null ? enemy.MoveSpeed    : 0f;
        float savedCooldown = enemy != null ? enemy.AttackCooldown : 1f;
        if (enemy != null) { enemy.MoveSpeed = 0f; enemy.AttackCooldown = float.MaxValue; }

        yield return new WaitForSeconds(duration);

        if (!health.IsDead && enemy != null)
        { enemy.MoveSpeed = savedSpeed; enemy.AttackCooldown = savedCooldown; }
        stunRoutine = null;
        RefreshTint();
    }

    private IEnumerator PoisonRoutine(float damage, float duration)
    {
        SetTint(new Color(0.4f, 1f, 0.3f));
        float elapsed = 0f;

        while (elapsed < duration)
        {
            yield return new WaitForSeconds(1f);
            elapsed += 1f;
            if (health.IsDead) break;
            health.TakeDamage(damage * 0.05f * poisonStacks);
        }
        poisonStacks  = 0;
        poisonRoutine = null;
        RefreshTint();
    }

    private IEnumerator BleedRoutine(float damage, float duration)
    {
        SetTint(new Color(0.9f, 0.1f, 0.2f)); // deep red
        float elapsed  = 0f;
        float tickDmg  = damage * 0.06f;
        const float Interval = 0.5f;

        while (elapsed < duration)
        {
            yield return new WaitForSeconds(Interval);
            elapsed += Interval;
            if (health.IsDead) break;
            health.TakeDamage(tickDmg);
            tickDmg *= 1.15f; // escalate 15% each tick
        }
        bleedRoutine = null;
        RefreshTint();
    }

    private IEnumerator RootRoutine(float duration)
    {
        SetTint(new Color(0.2f, 0.8f, 0.3f)); // green
        float savedSpeed = enemy != null ? enemy.MoveSpeed : 0f;
        if (enemy != null) enemy.MoveSpeed = 0f;
        // AttackCooldown is NOT changed — enemy can still attack

        yield return new WaitForSeconds(duration);

        if (!health.IsDead && enemy != null) enemy.MoveSpeed = savedSpeed;
        rootRoutine = null;
        RefreshTint();
    }

    private IEnumerator WeaknessRoutine(float duration)
    {
        SetTint(new Color(0.9f, 0.5f, 0.1f)); // amber
        if (health != null) health.DamageMultiplier = 1.5f;

        yield return new WaitForSeconds(duration);

        if (!health.IsDead && health != null) health.DamageMultiplier = 1f;
        weaknessRoutine = null;
        RefreshTint();
    }

    private IEnumerator CurseRoutine(float duration)
    {
        SetTint(new Color(0.6f, 0.1f, 0.8f)); // purple
        float savedSpeed = enemy != null ? enemy.MoveSpeed : 0f;
        curseSpeed = savedSpeed;
        isCursed   = true;
        if (enemy != null) enemy.MoveSpeed = 0f; // freeze AI movement; FixedUpdate drives fleeing

        yield return new WaitForSeconds(duration);

        isCursed = false;
        if (!health.IsDead && enemy != null) enemy.MoveSpeed = savedSpeed;
        curseRoutine = null;
        RefreshTint();
    }

    private IEnumerator BlindRoutine(float duration)
    {
        SetTint(new Color(0.95f, 0.95f, 0.6f)); // pale yellow
        float savedRange = enemy != null ? enemy.DetectionRange : 5f;
        if (enemy != null) enemy.DetectionRange = 0f; // can't detect player → enters Lost → wanders

        yield return new WaitForSeconds(duration);

        if (!health.IsDead && enemy != null) enemy.DetectionRange = savedRange;
        blindRoutine = null;
        RefreshTint();
    }

    // ── Tint helpers ──────────────────────────────────────────────────────────

    private void SetTint(Color color)
    {
        if (spriteRenderer != null) spriteRenderer.color = color;
    }

    /// <summary>Restores original color only when every effect has expired.</summary>
    private void RefreshTint()
    {
        if (burnRoutine    != null || freezeRoutine   != null ||
            slowRoutine    != null || stunRoutine      != null ||
            poisonRoutine  != null || bleedRoutine     != null ||
            rootRoutine    != null || weaknessRoutine  != null ||
            curseRoutine   != null || blindRoutine     != null)
            return;

        if (spriteRenderer != null) spriteRenderer.color = originalColor;
    }
}
