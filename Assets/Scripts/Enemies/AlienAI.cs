using Pathfinding;
using UnityEngine;

// Alien AI — wanders randomly, stands still between wanders, attacks with a
// crystalline projectile when the player enters range. Does NOT chase.
public class AlienAI : EnemyBase
{
    [Header("Animation Clip Prefixes")]
    [SerializeField] private string walkPrefix   = "alien_walk";
    [SerializeField] private string attackPrefix = "alien_attack";

    [Header("Wander")]
    [SerializeField] private float minWanderTime = 1.0f;
    [SerializeField] private float maxWanderTime = 3.0f;
    [SerializeField] private float minIdleTime   = 1.0f;
    [SerializeField] private float maxIdleTime   = 3.0f;
    [SerializeField] private float wanderRadius  = 6f;

    [Header("Animation Timing")]
    [SerializeField] private float attackAnimDuration = 0.6f;
    [SerializeField] private float damageHitFrame     = 0.3f;

    [Header("Projectile")]
    [SerializeField] private float projectileSpeed  = 7f;
    [SerializeField] private Color projectileColorA = new Color(0.4f, 1.0f, 0.9f, 1f); // cyan
    [SerializeField] private Color projectileColorB = new Color(0.8f, 0.4f, 1.0f, 1f); // violet

    [Header("Hit Effect Colours")]
    [SerializeField] private Color hitColorA = new Color(0.4f, 1.0f, 0.9f, 1f);
    [SerializeField] private Color hitColorB = new Color(0.8f, 0.4f, 1.0f, 1f);

    // Public properties for runtime scaling
    public string WalkPrefix         { get => walkPrefix;         set => walkPrefix         = value; }
    public string AttackPrefix       { get => attackPrefix;       set => attackPrefix       = value; }
    public float  MinWanderTime      { get => minWanderTime;      set => minWanderTime      = Mathf.Max(0.1f, value); }
    public float  MaxWanderTime      { get => maxWanderTime;      set => maxWanderTime      = Mathf.Max(0.1f, value); }
    public float  MinIdleTime        { get => minIdleTime;        set => minIdleTime        = Mathf.Max(0.1f, value); }
    public float  MaxIdleTime        { get => maxIdleTime;        set => maxIdleTime        = Mathf.Max(0.1f, value); }
    public float  AttackAnimDuration { get => attackAnimDuration; set => attackAnimDuration = Mathf.Max(0.05f, value); }
    public float  DamageHitFrame     { get => damageHitFrame;     set => damageHitFrame     = Mathf.Max(0f, value); }
    public float  ProjectileSpeed    { get => projectileSpeed;    set => projectileSpeed    = Mathf.Max(1f, value); }
    public Color  ProjectileColorA   { get => projectileColorA;   set => projectileColorA   = value; }
    public Color  ProjectileColorB   { get => projectileColorB;   set => projectileColorB   = value; }
    public Color  HitColorA          { get => hitColorA;          set => hitColorA          = value; }
    public Color  HitColorB          { get => hitColorB;          set => hitColorB          = value; }

    private enum State { Wander, Idle, Attack }
    private State   state = State.Wander;
    private float   stateTimer;
    private bool    damageDealt;
    private string  currentClip = "";

    // Rotation sprites for idle (same pattern as MeleeChaseAI)
    private static readonly string[] DirOrder =
        { "north", "north_east", "east", "south_east", "south", "south_west", "west", "north_west" };
    private static readonly string[] RotationDirFiles =
        { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
    [SerializeField] private Sprite[] rotationSprites = new Sprite[8];
    private SpriteRenderer sr;
    private bool    isIdling;
    private Vector2 idleBasePos;

    private PlayerHitEffect playerHitEffect;

    protected override void Start()
    {
        base.Start();
        sr = GetComponent<SpriteRenderer>();
        LoadRotationSprites();
        attackTimer = AttackCooldown; // ready to attack on first contact
        if (player != null)
            playerHitEffect = player.GetComponent<PlayerHitEffect>();
        EnterWander();
    }

    private void Update()
    {
        if (health.IsDead) return;
        stateTimer  -= Time.deltaTime;
        attackTimer += Time.deltaTime;

        switch (state)
        {
            case State.Wander: UpdateWander(); break;
            case State.Idle:   UpdateIdle();   break;
            case State.Attack: UpdateAttack(); break;
        }
    }

    private void FixedUpdate()
    {
        if (health.IsDead || state != State.Wander) return;

        Vector2 moveDir  = GetNextPathDirection();
        if (moveDir == Vector2.zero) return; // path complete — UpdateWander handles transition

        Vector2 finalDir = (moveDir + GetSeparationForce()).normalized;

        rb.MovePosition(rb.position + finalDir * MoveSpeed * Time.fixedDeltaTime);

        if (Vector2.Angle(currentDir, finalDir) > 22.5f)
        {
            currentDir = finalDir;
            PlayClip($"{walkPrefix}_{GetDirectionKey(currentDir)}");
        }
    }

    // ── Wander ──────────────────────────────────────────────────────────────

    private void EnterWander()
    {
        state      = State.Wander;
        stateTimer = Random.Range(minWanderTime, maxWanderTime);

        Vector2 dest    = PickRandomWalkablePoint();
        StartPathTo(dest);
        Vector2 initDir = (dest - rb.position).sqrMagnitude > 0.01f ? (dest - rb.position).normalized : Vector2.down;
        currentDir = initDir;

        if (isIdling) { isIdling = false; animator.enabled = true; }
        PlayClip($"{walkPrefix}_{GetDirectionKey(currentDir)}");
    }

    private void UpdateWander()
    {
        TryAttack();
        if (state == State.Attack) return;
        if (stateTimer <= 0f || PathComplete) EnterIdle();
    }

    // ── Idle ─────────────────────────────────────────────────────────────────

    private void EnterIdle()
    {
        state       = State.Idle;
        stateTimer  = Random.Range(minIdleTime, maxIdleTime);
        idleBasePos = rb.position;
        isIdling    = true;

        animator.enabled = false;
        UpdateIdleSprite();
    }

    private void UpdateIdle()
    {
        TryAttack();
        if (state == State.Attack) return;

        if (player != null) UpdateIdleSprite();

        float floatY = Mathf.Sin(Time.time * 1.5f * Mathf.PI * 2f) * 0.015f;
        rb.MovePosition(new Vector2(idleBasePos.x, idleBasePos.y + floatY));

        if (stateTimer <= 0f) EnterWander();
    }

    private void UpdateIdleSprite()
    {
        if (sr == null || rotationSprites == null || rotationSprites.Length < 8 || player == null) return;
        int idx = System.Array.IndexOf(DirOrder, GetDirectionKey(DirectionToPlayer()));
        if (idx >= 0 && rotationSprites[idx] != null)
            sr.sprite = rotationSprites[idx];
    }

    // ── Attack ────────────────────────────────────────────────────────────────

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
        }

        state       = State.Attack;
        attackTimer = 0f;
        damageDealt = false;
        currentDir  = DirectionToPlayer();

        // Tentacle whip visual — dark purple/indigo, independent of projectile colours
        Vector3 tentacleOrigin = transform.position + (Vector3)((Vector2)currentDir * 0.2f);
        TentacleWhip.Spawn(tentacleOrigin, currentDir,
                           colorA: new Color(0.25f, 0.0f, 0.45f, 1f), // dark purple
                           colorB: new Color(0.12f, 0.0f, 0.35f, 1f), // deep indigo
                           coilDuration: damageHitFrame);

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
            attackTimer = 0f; // begin cooldown
            EnterWander();
        }
    }

    protected override void Attack()
    {
        if (playerHealth == null || playerHealth.IsDead || player == null) return;
        Vector2 dir      = DirectionToPlayer();
        Vector3 spawnPos = transform.position + (Vector3)(dir * 0.45f);
        FireballProjectile.Spawn(spawnPos, dir, AttackDamage,
                                 projectileColorA, projectileColorB,
                                 playerHealth, playerHitEffect, projectileSpeed);
    }

    // ── Pathfinding ──────────────────────────────────────────────────────────

    private Vector2 PickRandomWalkablePoint()
    {
        if (AstarPath.active == null) return rb.position;

        for (int i = 0; i < 10; i++)
        {
            Vector2   candidate = rb.position + Random.insideUnitCircle * wanderRadius;
            GraphNode node      = AstarPath.active.GetNearest(candidate, NNConstraint.Default).node;
            if (node != null && node.Walkable)
                return (Vector3)node.position;
        }
        return rb.position;
    }

    // ── Rotation sprite loading ───────────────────────────────────────────────

    private void LoadRotationSprites()
    {
        if (rotationSprites != null && rotationSprites.Length == 8 && rotationSprites[0] != null) return;
#if UNITY_EDITOR
        string enemyName = gameObject.name.Replace("(Clone)", "").Replace(" Variant", "").Trim();
        rotationSprites = new Sprite[8];
        for (int i = 0; i < 8; i++)
        {
            string path = $"Assets/Art/Sprites/Enemies/{enemyName}/rotations/{RotationDirFiles[i]}.png";
            rotationSprites[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (rotationSprites[i] == null)
                Debug.LogWarning($"{name}: rotation sprite not found at {path}");
        }
#endif
    }

#if UNITY_EDITOR
    [ContextMenu("Bake Rotation Sprites")]
    private void BakeRotationSprites()
    {
        string enemyName = gameObject.name.Replace("(Clone)", "").Replace(" Variant", "").Trim();
        rotationSprites = new Sprite[8];
        for (int i = 0; i < 8; i++)
        {
            string path = $"Assets/Art/Sprites/Enemies/{enemyName}/rotations/{RotationDirFiles[i]}.png";
            rotationSprites[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        UnityEditor.EditorUtility.SetDirty(this);
        int loaded = System.Array.FindAll(rotationSprites, s => s != null).Length;
        Debug.Log($"Baked {loaded}/8 rotation sprites for {enemyName}");
    }
#endif

    private void PlayClip(string clip)
    {
        if (clip == currentClip) return;
        currentClip = clip;
        animator.Play(clip);
    }
}
