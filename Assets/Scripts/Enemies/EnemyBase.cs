using UnityEngine;

// Base class for all enemies. Handles movement, direction, and shared state.
// All combat stats are exposed via public properties for runtime scaling.
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Health))]
public abstract class EnemyBase : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2.5f;

    [Header("Attack")]
    [SerializeField] private float attackRange    = 1.2f;
    [SerializeField] private float attackDamage   = 10f;
    [SerializeField] private float attackCooldown = 1.5f;

    // Public read/write — safe to modify at any time (e.g. auto-scaling)
    public float MoveSpeed      { get => moveSpeed;      set => moveSpeed      = Mathf.Max(0f,    value); }
    public float AttackDamage   { get => attackDamage;   set => attackDamage   = Mathf.Max(0f,    value); }
    public float AttackCooldown { get => attackCooldown; set => attackCooldown = Mathf.Max(0.05f, value); }

    // Read-only at runtime — set in Inspector only
    public float AttackRange => attackRange;

    protected Rigidbody2D rb;
    protected Animator    animator;
    protected Health      health;
    protected Transform   player;
    protected Health      playerHealth;

    protected float   attackTimer;
    protected Vector2 currentDir = Vector2.down;

    // Read-only references for scaling systems that need to inspect state
    public Health HealthComponent => health;
    public bool   IsDead          => health != null && health.IsDead;

    protected virtual void Awake()
    {
        rb       = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        health   = GetComponent<Health>();
    }

    protected virtual void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player       = playerObj.transform;
            playerHealth = playerObj.GetComponent<Health>();
        }
        else
        {
            Debug.LogWarning($"{name}: No GameObject tagged 'Player' found.");
        }

        health.OnDeath.AddListener(OnDeath);
        EnsureSafeSpawnDistance();
    }

    private void EnsureSafeSpawnDistance()
    {
        if (player == null) return;
        float safeDistance = attackRange + 1f;
        if (DistanceToPlayer() < safeDistance)
        {
            Vector2 awayFromPlayer = -DirectionToPlayer();
            Vector2 newPos = (Vector2)player.position + awayFromPlayer * safeDistance;
            rb.position = newPos;
            transform.position = newPos;
            Physics2D.SyncTransforms();
        }
    }

    protected float DistanceToPlayer()
    {
        if (player == null) return float.MaxValue;
        return Vector2.Distance(rb.position, player.position);
    }

    protected Vector2 DirectionToPlayer()
    {
        if (player == null) return Vector2.zero;
        return ((Vector2)player.position - rb.position).normalized;
    }

    protected string GetDirectionKey(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (angle < 0) angle += 360f;
        if (angle >= 337.5f || angle < 22.5f)  return "east";
        if (angle < 67.5f)                      return "north_east";
        if (angle < 112.5f)                     return "north";
        if (angle < 157.5f)                     return "north_west";
        if (angle < 202.5f)                     return "west";
        if (angle < 247.5f)                     return "south_west";
        if (angle < 292.5f)                     return "south";
        return                                         "south_east";
    }

    protected abstract void Attack();

    protected virtual void OnDeath()
    {
        Destroy(gameObject, 0.1f);
    }
}
