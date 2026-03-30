using UnityEngine;

// Ghost AI — moves directly through walls when wandering (ignores pathfinding for movement).
// Attacks with a homing goo projectile that leaves a freeze zone.
// Wander + detection are handled by EnemyBase. canSeeThoughWalls = true.
public class GhostAI : EnemyBase
{
    [Header("Animation Timing")]
    [SerializeField] private float attackAnimDuration = 0.6f;
    [SerializeField] private float damageHitFrame     = 0.3f;
    [SerializeField] private float conjureDuration    = 0.25f;

    [Header("Projectile")]
    [SerializeField] private float projectileSpeed  = 5f;
    [SerializeField] private Color projectileColorA = new Color(0.55f, 1.00f, 0.72f, 1f); // spectral ectoplasm green
    [SerializeField] private Color projectileColorB = new Color(0.30f, 0.88f, 0.55f, 1f);

    [Header("Freeze Zone")]
    [SerializeField] private float zoneDuration = 5f;
    [SerializeField] private float zoneRadius   = 1.3f;

    [Header("Hit Effect Colours")]
    [SerializeField] private Color hitColorA = new Color(0.55f, 1.00f, 0.72f, 1f);
    [SerializeField] private Color hitColorB = new Color(0.30f, 0.88f, 0.55f, 1f);

    public float AttackAnimDuration { get => attackAnimDuration; set => attackAnimDuration = Mathf.Max(0.05f, value); }
    public float DamageHitFrame     { get => damageHitFrame;     set => damageHitFrame     = Mathf.Max(0f, value); }
    public float ConjureDuration    { get => conjureDuration;    set => conjureDuration    = Mathf.Max(0f, value); }
    public float ProjectileSpeed    { get => projectileSpeed;    set => projectileSpeed    = Mathf.Max(1f, value); }
    public Color ProjectileColorA   { get => projectileColorA;   set => projectileColorA   = value; }
    public Color ProjectileColorB   { get => projectileColorB;   set => projectileColorB   = value; }
    public Color HitColorA          { get => hitColorA;          set => hitColorA          = value; }
    public Color HitColorB          { get => hitColorB;          set => hitColorB          = value; }
    public float ZoneDuration       { get => zoneDuration;       set => zoneDuration       = Mathf.Max(0f, value); }
    public float ZoneRadius         { get => zoneRadius;         set => zoneRadius         = Mathf.Max(0f, value); }

    [Header("Pathfinding")]
    [SerializeField] private float pathRefreshInterval = 0.3f;

    private bool                damageDealt;
    private bool                isAttacking;
    private GhostGooProjectile  activeProjectile;
    private float               pathTimer;

    protected override void Start()
    {
        base.Start();
        CanSeeThoughWalls = true;
        attackTimer = AttackCooldown;
    }

    // ── Wander overrides: Ghost moves directly, ignoring walls ────────────────

    protected override void OnWanderDestinationSet(Vector2 destination)
    {
        // Ghost moves directly to destination — no A* path needed.
    }

    protected override Vector2 GetWanderMoveDirection()
    {
        Vector2 toTarget = WanderTarget - rb.position;
        if (toTarget.sqrMagnitude < 0.09f) return Vector2.zero;
        return toTarget.normalized;
    }

    // ── Active state ──────────────────────────────────────────────────────────

    protected override void OnActivated()
    {
        isAttacking = false;
        damageDealt = false;
        pathTimer   = pathRefreshInterval;
    }

    protected override void OnDeactivated()
    {
        isAttacking = false;
    }

    protected override void UpdateActive()
    {
        if (isAttacking)
        {
            UpdateAttack();
            return;
        }

        if (DistanceToPlayer() > AttackRange)
        {
            // Ghost moves directly through walls — no A* needed, just move straight
            pathTimer += Time.deltaTime;
            if (pathTimer >= pathRefreshInterval || PathComplete)
            {
                pathTimer = 0f;
                StartPathTo(player.position);
            }

            Vector2 moveDir = GetNextPathDirection();
            if (moveDir == Vector2.zero) moveDir = LastPathDir;
            if (moveDir == Vector2.zero) moveDir = DirectionToPlayer();

            rb.MovePosition(rb.position + moveDir * MoveSpeed * Time.fixedDeltaTime);
            UpdateDirectionSprite(GetDirectionKey(moveDir));
        }

        TryAttack();
    }

    private void TryAttack()
    {
        if (player == null || playerHealth == null || playerHealth.IsDead) return;
        if (DistanceToPlayer() > AttackRange || attackTimer < AttackCooldown) return;
        EnterAttack();
    }

    private void EnterAttack()
    {
        isAttacking = true;
        attackTimer = 0f;
        damageDealt = false;
        currentDir  = DirectionToPlayer();

        SFXManager.Instance?.PlayGhostThrow((Vector2)transform.position);

        Vector3 spawnPos = transform.position + (Vector3)(currentDir * 0.45f);
        activeProjectile = GhostGooProjectile.Conjure(spawnPos, AttackDamage,
                               projectileColorA, projectileColorB,
                               playerHealth, playerHitEffect, player,
                               zoneDuration, zoneRadius, conjureDuration);

        PlayClip($"{AttackPrefix}_{GetDirectionKey(currentDir)}");
    }

    private void UpdateAttack()
    {
        if (!damageDealt && attackTimer >= damageHitFrame)
        {
            damageDealt = true;
            Attack();
        }
        if (attackTimer >= attackAnimDuration)
        {
            attackTimer = 0f;
            isAttacking = false;
        }
    }

    protected override void Attack()
    {
        if (playerHealth == null || playerHealth.IsDead || player == null) return;
        if (activeProjectile == null) return;
        activeProjectile.Launch(player, projectileSpeed);
        activeProjectile = null;
    }
}
