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
    [SerializeField] private float floatAmplitude  = 0.015f; // world units
    [SerializeField] private float floatFrequency  = 1.5f;  // cycles per second

    [Header("Hit Effect Colours")]
    [SerializeField] private Color hitColorA = new Color(0.85f, 0.85f, 0.88f, 1f);
    [SerializeField] private Color hitColorB = new Color(0.45f, 0.45f, 0.50f, 1f);

    [Header("Pathfinding")]
    [SerializeField] private float pathRefreshInterval = 0.3f;

    // Auto-loaded from Assets/Art/Sprites/Enemies/{GameObjectName}/rotations/ at Start.
    // Right-click the component → "Bake Rotation Sprites" before building to serialize them.
    [SerializeField] private Sprite[] rotationSprites = new Sprite[8];

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

    // Direction order matching rotationSprites array slots
    private static readonly string[] DirOrder =
        { "north", "north_east", "east", "south_east", "south", "south_west", "west", "north_west" };

    private SpriteRenderer sr;
    private string currentClip = "";
    private bool damageDealt;
    private bool isIdling;
    private Vector2 idleBasePos;
    private PlayerHitEffect playerHitEffect;
    private float pathTimer;

    protected override void Start()
    {
        base.Start();
        sr = GetComponent<SpriteRenderer>();
        LoadRotationSprites();
        attackTimer = AttackCooldown; // first attack fires immediately when in range
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

        pathTimer += Time.deltaTime;
        if ((pathTimer >= pathRefreshInterval || PathComplete) && state == State.Walk)
        {
            pathTimer = 0f;
            StartPathTo(player.position);
        }

        switch (state)
        {
            case State.Walk:   UpdateWalk();   break;
            case State.Attack: UpdateAttack(); break;
        }
    }

    private void FixedUpdate()
    {
        if (state != State.Walk || player == null || health.IsDead) return;

        if (DistanceToPlayer() <= AttackRange)
        {
            if (isIdling)
            {
                float floatY = Mathf.Sin(Time.time * floatFrequency * Mathf.PI * 2f) * floatAmplitude;
                rb.MovePosition(new Vector2(idleBasePos.x, idleBasePos.y + floatY));
            }
            return;
        }

        Vector2 moveDir = GetNextPathDirection();
        if (moveDir == Vector2.zero) moveDir = LastPathDir;
        if (moveDir == Vector2.zero) moveDir = DirectionToPlayer();

        Vector2 finalDir = (moveDir + GetSeparationForce()).normalized;

        rb.MovePosition(rb.position + finalDir * MoveSpeed * Time.fixedDeltaTime);

        if (Vector2.Angle(currentDir, finalDir) > directionChangeThreshold)
        {
            currentDir = finalDir;
            PlayWalk();
        }
    }

    private void UpdateWalk()
    {
        bool inRange = DistanceToPlayer() <= AttackRange;

        if (inRange && !isIdling)
        {
            isIdling = true;
            idleBasePos = rb.position;
            animator.enabled = false; // hand control of sprite to us
        }
        else if (!inRange && isIdling)
        {
            isIdling = false;
            rb.MovePosition(idleBasePos); // snap back to ground level before walking
            animator.enabled = true;
            PlayWalk();
        }

        if (isIdling)
            UpdateIdleSprite();

        if (inRange && attackTimer >= AttackCooldown)
            EnterAttack();
    }

    private void UpdateIdleSprite()
    {
        if (sr == null || rotationSprites == null || rotationSprites.Length < 8) return;
        int idx = System.Array.IndexOf(DirOrder, GetDirectionKey(DirectionToPlayer()));
        if (idx >= 0 && rotationSprites[idx] != null)
            sr.sprite = rotationSprites[idx];
    }

    private void EnterAttack()
    {
        isIdling = false;
        animator.enabled = true;
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
            attackTimer = 0f; // reset so next attack waits the full cooldown
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

    private static readonly string[] RotationDirFiles =
        { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

    private void LoadRotationSprites()
    {
        // Already baked (e.g. in a build) — skip
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
}
