using System.Collections;
using UnityEngine;

// Shared AI for melee enemies that chase the player and attack at close range.
// Set walkPrefix/attackPrefix to match the animation clip names in the controller.
// e.g. walkPrefix = "bear_walk", attackPrefix = "bear_attack"
public class MeleeChaseAI : EnemyBase
{
    [Header("Animation Clip Prefixes")]
    [SerializeField] private string walkPrefix   = "walk";
    [SerializeField] private string attackPrefix = "attack";

    [Header("Animation Timing")]
    [SerializeField] private float attackAnimDuration = 0.6f;
    [SerializeField] private float damageHitFrame     = 0.3f;
    [SerializeField] private float hitEffectDelay     = 0.25f;

    [Header("Animation")]
    [SerializeField] private float directionChangeThreshold = 22.5f;

    [Header("Hit Effect Colours")]
    [SerializeField] private Color hitColorA = new Color(0.85f, 0.85f, 0.88f, 1f);
    [SerializeField] private Color hitColorB = new Color(0.45f, 0.45f, 0.50f, 1f);

    // Public properties — expose all tunable values for runtime scaling
    public string WalkPrefix   { get => walkPrefix;   set => walkPrefix   = value; }
    public string AttackPrefix { get => attackPrefix; set => attackPrefix = value; }
    public float AttackAnimDuration      { get => attackAnimDuration;      set => attackAnimDuration      = Mathf.Max(0.05f, value); }
    public float DamageHitFrame          { get => damageHitFrame;          set => damageHitFrame          = Mathf.Max(0f,    value); }
    public float HitEffectDelay          { get => hitEffectDelay;          set => hitEffectDelay          = Mathf.Max(0f,    value); }
    public float DirectionChangeThreshold{ get => directionChangeThreshold;set => directionChangeThreshold= Mathf.Clamp(value, 0f, 180f); }
    public Color HitColorA { get => hitColorA; set => hitColorA = value; }
    public Color HitColorB { get => hitColorB; set => hitColorB = value; }

    private enum State { Walk, Attack }
    private State state = State.Walk;

    private string currentClip = "";
    private bool damageDealt;
    private PlayerHitEffect playerHitEffect;

    protected override void Start()
    {
        base.Start();
        if (player != null)
        {
            playerHitEffect = player.GetComponent<PlayerHitEffect>();
            currentDir = DirectionToPlayer();
            PlayWalk();
        }
    }

    private void Update()
    {
        if (player == null || health.IsDead) return;
        attackTimer += Time.deltaTime;

        switch (state)
        {
            case State.Walk:   UpdateWalk();   break;
            case State.Attack: UpdateAttack(); break;
        }
    }

    private void FixedUpdate()
    {
        if (state != State.Walk || player == null || health.IsDead) return;

        Vector2 toPlayer = DirectionToPlayer();
        rb.MovePosition(rb.position + toPlayer * MoveSpeed * Time.fixedDeltaTime);

        if (Vector2.Angle(currentDir, toPlayer) > directionChangeThreshold)
        {
            currentDir = toPlayer;
            PlayWalk();
        }
    }

    private void UpdateWalk()
    {
        if (DistanceToPlayer() <= AttackRange && attackTimer >= AttackCooldown)
            EnterAttack();
    }

    private void EnterAttack()
    {
        state = State.Attack;
        attackTimer = 0f;
        damageDealt = false;
        currentDir = DirectionToPlayer();
        PlayClip($"{attackPrefix}_{GetDirectionKey(currentDir)}");
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
            state = State.Walk;
            attackTimer = AttackCooldown;
            PlayWalk();
        }
    }

    protected override void Attack()
    {
        if (playerHealth == null || playerHealth.IsDead) return;
        if (DistanceToPlayer() > AttackRange * 1.3f) return;

        playerHealth.TakeDamage(AttackDamage);
        StartCoroutine(DelayedHitEffects());
    }

    private IEnumerator DelayedHitEffects()
    {
        yield return new WaitForSeconds(hitEffectDelay);
        if (player != null)
            HitEffectSpawner.SpawnHit(player.position, hitColorA, hitColorB);
        playerHitEffect?.PlayHitEffect();
    }

    private void PlayWalk() => PlayClip($"{walkPrefix}_{GetDirectionKey(currentDir)}");

    private void PlayClip(string clip)
    {
        if (clip == currentClip) return;
        currentClip = clip;
        animator.Play(clip);
    }
}
