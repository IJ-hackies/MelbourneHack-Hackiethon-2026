using UnityEngine;

// Alien AI — pathfinds toward the player until in attack range, then stands and fires.
public class AlienAI : EnemyBase
{
    [Header("Animation Timing")]
    [SerializeField] private float attackAnimDuration = 0.6f;
    [SerializeField] private float damageHitFrame     = 0.3f;

    [Header("Projectile")]
    [SerializeField] private float projectileSpeed  = 7f;
    [SerializeField] private Color projectileColorA = new Color(0.4f, 1.0f, 0.9f, 1f);
    [SerializeField] private Color projectileColorB = new Color(0.8f, 0.4f, 1.0f, 1f);

    [Header("Hit Effect Colours")]
    [SerializeField] private Color hitColorA = new Color(0.4f, 1.0f, 0.9f, 1f);
    [SerializeField] private Color hitColorB = new Color(0.8f, 0.4f, 1.0f, 1f);

    public float AttackAnimDuration { get => attackAnimDuration; set => attackAnimDuration = Mathf.Max(0.05f, value); }
    public float DamageHitFrame     { get => damageHitFrame;     set => damageHitFrame     = Mathf.Max(0f, value); }
    public float ProjectileSpeed    { get => projectileSpeed;    set => projectileSpeed    = Mathf.Max(1f, value); }
    public Color ProjectileColorA   { get => projectileColorA;   set => projectileColorA   = value; }
    public Color ProjectileColorB   { get => projectileColorB;   set => projectileColorB   = value; }
    public Color HitColorA          { get => hitColorA;          set => hitColorA          = value; }
    public Color HitColorB          { get => hitColorB;          set => hitColorB          = value; }

    [Header("Pathfinding")]
    [SerializeField] private float pathRefreshInterval = 0.3f;

    private bool isAttacking;
    private bool damageDealt;
    private float pathTimer;

    private bool    isIdling;
    private Vector2 idleBasePos;

    protected override void Start()
    {
        base.Start();
        CanSeeThoughWalls = true;
        attackTimer = AttackCooldown;
    }

    protected override void OnActivated()
    {
        isAttacking = false;
        isIdling    = false;
        pathTimer   = pathRefreshInterval;
    }

    protected override void OnDeactivated()
    {
        isAttacking = false;
        isIdling    = false;
    }

    protected override void UpdateActive()
    {
        if (isAttacking)
        {
            UpdateAttack();
            return;
        }

        bool inRange = DistanceToPlayer() <= AttackRange;

        if (inRange)
        {
            // In range — idle float bob and face the player
            if (!isIdling)
            {
                isIdling         = true;
                idleBasePos      = rb.position;
                animator.enabled = false;
            }

            float floatY = Mathf.Sin(Time.time * 1.5f * Mathf.PI * 2f) * 0.015f;
            rb.MovePosition(new Vector2(idleBasePos.x, idleBasePos.y + floatY));

            if (player != null)
                UpdateDirectionSprite(GetDirectionKey(DirectionToPlayer()));

            TryAttack();
        }
        else
        {
            // Not in range — pathfind toward the player
            if (isIdling)
            {
                isIdling         = false;
                animator.enabled = true;
                currentClip      = "";
            }

            pathTimer += Time.deltaTime;
            if (pathTimer >= pathRefreshInterval || PathComplete)
            {
                pathTimer = 0f;
                StartPathTo(player.position);
            }

            Vector2 moveDir  = GetNextPathDirection();
            if (moveDir == Vector2.zero) moveDir = LastPathDir;
            if (moveDir == Vector2.zero) moveDir = DirectionToPlayer();

            rb.MovePosition(rb.position + moveDir * MoveSpeed * Time.fixedDeltaTime);
            UpdateDirectionSprite(GetDirectionKey(moveDir));

            TryAttack();
        }
    }

    private void TryAttack()
    {
        if (player == null || playerHealth == null || playerHealth.IsDead) return;
        if (DistanceToPlayer() > AttackRange || attackTimer < AttackCooldown) return;
        EnterAttack();
    }

    private void EnterAttack()
    {
        if (isIdling)
        {
            isIdling         = false;
            animator.enabled = true;
            currentClip      = "";
        }

        isAttacking = true;
        attackTimer = 0f;
        damageDealt = false;
        currentDir  = DirectionToPlayer();

        Vector3 tentacleOrigin = transform.position + (Vector3)((Vector2)currentDir * 0.2f);
        TentacleWhip.Spawn(tentacleOrigin, currentDir,
                           colorA: new Color(0.25f, 0.0f, 0.45f, 1f),
                           colorB: new Color(0.12f, 0.0f, 0.35f, 1f),
                           coilDuration: damageHitFrame);

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
            isIdling    = false;
        }
    }

    protected override void Attack()
    {
        if (playerHealth == null || playerHealth.IsDead || player == null) return;
        Vector2 dir      = DirectionToPlayer();
        Vector3 spawnPos = transform.position + (Vector3)(dir * 0.45f);
        FireballProjectile.Spawn(spawnPos, dir, AttackDamage,
                                 projectileColorA, projectileColorB,
                                 playerHealth, playerHitEffect, projectileSpeed,
                                 poisonDuration: 2f,
                                 pierceWalls: true,
                                 style: ProjectileStyle.Alien);
    }
}
