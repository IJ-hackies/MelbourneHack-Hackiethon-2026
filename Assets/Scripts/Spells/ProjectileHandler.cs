using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attached to a projectile prefab. Initialized by SpellExecutor with the
/// casting spell's data and aim direction. Reads tags to modify behavior.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class ProjectileHandler : MonoBehaviour
{
    [SerializeField] private GameObject aoePrefab;

    private SpellData spell;
    private Vector2 direction;
    private Rigidbody2D rb;

    private bool initialized = false;
    private bool isSplitChild = false;
    private int hitCount = 0;
    private int bounceCount = 0;
    private const int maxPierceHits = 5;
    private const int maxBounces = 3;
    private const float lifetime = 5f;

    private Health playerHealth;
    private readonly HashSet<GameObject> chainedEnemies = new();

    private const int WallLayerMask = 1 << 9; // Walls layer

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            playerHealth = playerObj.GetComponent<Health>();
    }

    public void Init(SpellData spellData, Vector2 dir, bool splitChild = false)
    {
        spell = spellData;
        direction = dir;
        initialized = true;
        isSplitChild = splitChild;

        rb.linearVelocity = direction * spell.speed;
        Debug.Log($"[Projectile] Init called. dir={dir} speed={spell.speed} velocity={rb.linearVelocity} rbType={rb.bodyType}");

        if (spell.HasTag(SpellTag.STUTTER_MOTION))
            StartCoroutine(StutterMotion());

        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        if (!initialized) return;

        if (spell.HasTag(SpellTag.HOMING) || spell.HasTag(SpellTag.ENEMY_HOMING))
            ApplyHoming();

        if (spell.HasTag(SpellTag.SPIRAL))
            ApplySpiral();

        HandleWallInteraction();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!initialized) return;

        // Enemies put colliders on a child (HitBoxChild); walk up to find Health.
        // Exclude the player by comparing against the cached playerHealth reference.
        Health h = other.GetComponentInParent<Health>();
        if (h == null || h == playerHealth || h.IsDead) return;

        HitEnemy(h.gameObject);
    }

    private void HitEnemy(GameObject enemyObj)
    {
        Health enemyHealth = enemyObj.GetComponent<Health>();
        if (enemyHealth == null || enemyHealth.IsDead) return;

        enemyHealth.TakeDamage(spell.damage);

        if (spell.HasTag(SpellTag.LIFESTEAL))
            playerHealth?.Heal(spell.damage * 0.3f);

        ApplyStatusEffects(enemyObj);

        if (spell.HasTag(SpellTag.PUSH))
        {
            var enemyRb = enemyObj.GetComponent<Rigidbody2D>();
            enemyRb?.AddForce(rb.linearVelocity.normalized * 8f, ForceMode2D.Impulse);
        }

        if (spell.HasTag(SpellTag.PULL) && playerHealth != null)
        {
            var enemyRb = enemyObj.GetComponent<Rigidbody2D>();
            Vector2 toPlayer = ((Vector2)playerHealth.transform.position - (Vector2)enemyObj.transform.position).normalized;
            enemyRb?.AddForce(toPlayer * 10f, ForceMode2D.Impulse);
        }

        if (spell.HasTag(SpellTag.AOE_BURST))
            ApplyAoeBurst(transform.position);

        if (spell.HasTag(SpellTag.CHAIN))
        {
            chainedEnemies.Add(enemyObj);
            TryChain();
            return;
        }

        if (!isSplitChild && spell.HasTag(SpellTag.SPLIT_ON_IMPACT))
        {
            SpawnSplitProjectiles();
            Destroy(gameObject);
            return;
        }

        hitCount++;
        if (!spell.HasTag(SpellTag.PIERCE) || hitCount >= maxPierceHits)
            Destroy(gameObject);
    }

    // --- Movement tag behaviors ---

    private void ApplyHoming()
    {
        Transform target = spell.HasTag(SpellTag.ENEMY_HOMING)
            ? (playerHealth != null ? playerHealth.transform : null)
            : FindNearestEnemyTransform();

        if (target == null) return;

        Vector2 toTarget = ((Vector2)target.position - rb.position).normalized;
        float turnRad = 180f * Mathf.Deg2Rad * Time.fixedDeltaTime;
        Vector2 newDir = Vector2.MoveTowards(rb.linearVelocity.normalized, toTarget, turnRad);
        rb.linearVelocity = newDir.normalized * spell.speed;
    }

    private void ApplySpiral()
    {
        float rad = 90f * Mathf.Deg2Rad * Time.fixedDeltaTime;
        Vector2 v = rb.linearVelocity;
        rb.linearVelocity = new Vector2(
            v.x * Mathf.Cos(rad) - v.y * Mathf.Sin(rad),
            v.x * Mathf.Sin(rad) + v.y * Mathf.Cos(rad)
        );
    }

    private void HandleWallInteraction()
    {
        if (spell.HasTag(SpellTag.PIERCE_WALLS)) return;

        // Look ahead by 2 frames to predict wall contact before it happens
        Vector2 nextPos = rb.position + rb.linearVelocity * Time.fixedDeltaTime * 2f;
        RaycastHit2D hit = Physics2D.Linecast(rb.position, nextPos, WallLayerMask);
        if (hit.collider == null) return;

        if (spell.HasTag(SpellTag.WALL_BOUNCE) && bounceCount < maxBounces)
        {
            bounceCount++;
            rb.linearVelocity = Vector2.Reflect(rb.linearVelocity, hit.normal);
        }
        else if (!spell.HasTag(SpellTag.WALL_BOUNCE))
        {
            Destroy(gameObject);
        }
    }

    // --- Effect tag behaviors ---

    private void ApplyAoeBurst(Vector2 center)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, 3f, LayerMask.GetMask("Enemy"));
        foreach (var hit in hits)
        {
            var h = hit.GetComponent<Health>();
            if (h != null && !h.IsDead)
                h.TakeDamage(spell.damage * 0.5f);
        }
        if (aoePrefab != null)
            Destroy(Instantiate(aoePrefab, center, Quaternion.identity), 1f);
    }

    private void TryChain()
    {
        EnemyBase[] allEnemies = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);
        EnemyBase nextTarget = null;
        float nearest = float.MaxValue;

        foreach (var e in allEnemies)
        {
            if (e.IsDead || chainedEnemies.Contains(e.gameObject)) continue;
            float dist = Vector2.Distance(rb.position, e.transform.position);
            if (dist < nearest && dist <= 6f)
            {
                nearest = dist;
                nextTarget = e;
            }
        }

        if (nextTarget == null) { Destroy(gameObject); return; }

        rb.linearVelocity = ((Vector2)nextTarget.transform.position - rb.position).normalized * spell.speed;
    }

    private void SpawnSplitProjectiles()
    {
        float baseAngle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
        float[] offsets = { -30f, 0f, 30f };

        foreach (float offset in offsets)
        {
            float angle = (baseAngle + offset) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            GameObject child = Instantiate(gameObject, transform.position, Quaternion.identity);
            child.GetComponent<ProjectileHandler>()?.Init(spell, dir, splitChild: true);
        }
    }

    private void ApplyStatusEffects(GameObject enemyObj)
    {
        // Auto-add StatusEffectHandler if the enemy prefab doesn't have one yet
        var status = enemyObj.GetComponent<StatusEffectHandler>()
                     ?? enemyObj.AddComponent<StatusEffectHandler>();

        if (spell.HasTag(SpellTag.BURN))   status.ApplyBurn(spell.damage);
        if (spell.HasTag(SpellTag.FREEZE)) status.ApplyFreeze();
        if (spell.HasTag(SpellTag.SLOW))   status.ApplySlow();
        if (spell.HasTag(SpellTag.STUN))   status.ApplyStun();
        if (spell.HasTag(SpellTag.POISON)) status.ApplyPoison(spell.damage);
    }

    // --- Coroutines ---

    private IEnumerator StutterMotion()
    {
        while (initialized)
        {
            yield return new WaitForSeconds(0.15f);
            rb.linearVelocity = Vector2.zero;
            yield return new WaitForSeconds(0.1f);
            rb.linearVelocity = direction * spell.speed;
        }
    }

    // --- Helpers ---

    private Transform FindNearestEnemyTransform()
    {
        EnemyBase[] enemies = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);
        Transform nearest = null;
        float minDist = float.MaxValue;

        foreach (var e in enemies)
        {
            if (e.IsDead) continue;
            float d = Vector2.Distance(rb.position, e.transform.position);
            if (d < minDist) { minDist = d; nearest = e.transform; }
        }
        return nearest;
    }
}
