using System.Collections;
using UnityEngine;

// Melee enemy: chases the player and attacks at close range when active.
// Wander + detection are handled by EnemyBase.
public class MeleeChaseAI : EnemyBase
{
    [Header("Animation Timing")]
    [SerializeField] private float attackAnimDuration = 0.6f;
    [SerializeField] private float damageHitFrame     = 0.3f;
    [SerializeField] private float hitEffectDelay     = 0.25f;

    [Header("Animation")]
    [SerializeField] private float directionChangeThreshold = 22.5f;
    [SerializeField] private float floatAmplitude = 0.015f;
    [SerializeField] private float floatFrequency = 1.5f;

    [Header("Hit Effect Colours")]
    [SerializeField] private Color hitColorA = new Color(0.85f, 0.85f, 0.88f, 1f);
    [SerializeField] private Color hitColorB = new Color(0.45f, 0.45f, 0.50f, 1f);

    [Header("Attack SFX")]
    [SerializeField] private AudioClip attackSfxClip;
    [SerializeField, Range(0f, 1f)] private float attackSfxVolume = 0.8f;

    [Header("Status Effects")]
    [SerializeField] private bool  applyBleed            = false;
    [SerializeField] private float bleedDuration         = 2f;
    [SerializeField] private float bleedDamageMultiplier = 0.1f;

    [Header("Pathfinding")]
    [SerializeField] private float pathRefreshInterval = 0.3f;

    public float AttackAnimDuration       { get => attackAnimDuration;       set => attackAnimDuration       = Mathf.Max(0.05f, value); }
    public float DamageHitFrame           { get => damageHitFrame;           set => damageHitFrame           = Mathf.Max(0f,    value); }
    public float HitEffectDelay           { get => hitEffectDelay;           set => hitEffectDelay           = Mathf.Max(0f,    value); }
    public float DirectionChangeThreshold { get => directionChangeThreshold; set => directionChangeThreshold = Mathf.Clamp(value, 0f, 180f); }
    public Color HitColorA     { get => hitColorA;     set => hitColorA     = value; }
    public Color HitColorB     { get => hitColorB;     set => hitColorB     = value; }
    public float BleedDuration { get => bleedDuration; set => bleedDuration = Mathf.Max(0f, value); }

    private enum State { Walk, Attack }
    private State state = State.Walk;

    private bool    damageDealt;
    private bool    isIdling;
    private Vector2 idleBasePos;
    private float   pathTimer;

    protected override void Start()
    {
        base.Start();
        attackTimer = AttackCooldown;
        if (player != null)
            currentDir = DirectionToPlayer();

        ApplySpeedHealthBalance();
    }

    /// <summary>
    /// Slow melee enemies are tankier; fast ones are fragile.
    /// Uses a power curve so the effect is meaningful but not extreme:
    ///   speed 1.5 → ~1.4× HP  |  speed 2.5 (ref) → 1.0×  |  speed 4.0 → ~0.80×  |  speed 5.5 → ~0.68×
    /// Applied after base.Start() so stage-based HP scaling (EnemySpawner) is already baked in.
    /// </summary>
    private void ApplySpeedHealthBalance()
    {
        const float ReferenceSpeed = 2.5f; // default EnemyBase moveSpeed
        const float Exponent       = 0.65f; // soften the curve — pure inverse would be 1.0

        var hp = GetComponent<Health>();
        if (hp == null || MoveSpeed <= 0f) return;

        float mult = Mathf.Pow(ReferenceSpeed / MoveSpeed, Exponent);
        mult = Mathf.Clamp(mult, 0.5f, 2.5f); // hard cap: never below 50% or above 250% of base
        hp.SetMaxHealth(hp.Max * mult);
    }

    protected override void OnActivated()
    {
        state     = State.Walk;
        isIdling  = false;
        pathTimer = pathRefreshInterval; // request path immediately
        PlayClip($"{WalkPrefix}_{GetDirectionKey(currentDir)}");
    }

    protected override void OnDeactivated()
    {
        isIdling = false;
        state    = State.Walk;
    }

    protected override void UpdateActive()
    {
        pathTimer += Time.deltaTime;
        if ((pathTimer >= pathRefreshInterval || PathComplete) && state == State.Walk)
        {
            pathTimer = 0f;
            StartPathTo(player.position);
        }

        switch (state)
        {
            case State.Walk:   UpdateWalkActive();   break;
            case State.Attack: UpdateAttackActive(); break;
        }
    }

    protected override void FixedUpdateActive()
    {
        if (state != State.Walk || player == null) return;

        if (DistanceToPlayer() <= AttackRange)
        {
            if (isIdling)
            {
                float floatY = Mathf.Sin(Time.time * floatFrequency * Mathf.PI * 2f) * floatAmplitude;
                rb.MovePosition(new Vector2(idleBasePos.x, idleBasePos.y + floatY));
            }
            return;
        }

        Vector2 moveDir  = GetNextPathDirection();
        if (moveDir == Vector2.zero) moveDir = LastPathDir;
        if (moveDir == Vector2.zero) moveDir = DirectionToPlayer();

        Vector2 finalDir = (moveDir + GetSeparationForce()).normalized;
        rb.MovePosition(rb.position + finalDir * MoveSpeed * Time.fixedDeltaTime);

        if (Vector2.Angle(currentDir, finalDir) > directionChangeThreshold)
        {
            currentDir = finalDir;
            PlayClip($"{WalkPrefix}_{GetDirectionKey(currentDir)}");
        }
    }

    private void UpdateWalkActive()
    {
        bool inRange = DistanceToPlayer() <= AttackRange;

        if (inRange && !isIdling)
        {
            isIdling    = true;
            idleBasePos = rb.position;
            animator.enabled = false;
        }
        else if (!inRange && isIdling)
        {
            isIdling         = false;
            rb.MovePosition(idleBasePos);
            animator.enabled = true;
            currentClip      = "";
            PlayClip($"{WalkPrefix}_{GetDirectionKey(currentDir)}");
        }

        if (isIdling)
            UpdateDirectionSprite(GetDirectionKey(DirectionToPlayer()));

        if (inRange && attackTimer >= AttackCooldown)
            EnterAttack();
    }

    private void UpdateAttackActive()
    {
        if (!damageDealt && attackTimer >= damageHitFrame)
        {
            damageDealt = true;
            Attack();
        }
        if (attackTimer >= attackAnimDuration)
        {
            state      = State.Walk;
            attackTimer = 0f;
            isIdling   = false;
            animator.enabled = true;
            currentClip = "";
            PlayClip($"{WalkPrefix}_{GetDirectionKey(currentDir)}");
        }
    }

    private void EnterAttack()
    {
        isIdling         = false;
        animator.enabled = true;
        state            = State.Attack;
        attackTimer      = 0f;
        damageDealt      = false;
        currentDir       = DirectionToPlayer();
        PlayClip($"{AttackPrefix}_{GetDirectionKey(currentDir)}");
    }

    protected override void Attack()
    {
        if (playerHealth == null || playerHealth.IsDead) return;
        if (DistanceToPlayer() > AttackRange * 1.3f) return;

        SFXManager.Instance?.PlayAtPosition(attackSfxClip, attackSfxVolume, transform.position);
        DealDamageToPlayer(AttackDamage);
        if (applyBleed)
            playerStatusEffects?.ApplyBleed(AttackDamage * bleedDamageMultiplier, bleedDuration);
        StartCoroutine(DelayedHitEffects());
    }

    private IEnumerator DelayedHitEffects()
    {
        yield return new WaitForSeconds(hitEffectDelay);
        if (player != null)
            HitEffectSpawner.SpawnHit(player.position, hitColorA, hitColorB);
        playerHitEffect?.PlayHitEffect();
    }
}
