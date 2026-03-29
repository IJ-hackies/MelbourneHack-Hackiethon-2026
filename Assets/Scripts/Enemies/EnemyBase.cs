using System.Collections;
using System.Collections.Generic;
using Pathfinding;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(Seeker))]
public abstract class EnemyBase : MonoBehaviour
{
    // ── Combat Stats ──────────────────────────────────────────────────────────
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2.5f;

    [Header("Attack")]
    [SerializeField] private float attackRange    = 1.2f;
    [SerializeField] private float attackDamage   = 10f;
    [SerializeField] private float attackCooldown = 1.5f;

    public float MoveSpeed      { get => moveSpeed;      set => moveSpeed      = Mathf.Max(0f,    value); }
    public float AttackDamage   { get => attackDamage;   set => attackDamage   = Mathf.Max(0f,    value); }
    public float AttackCooldown { get => attackCooldown; set => attackCooldown = Mathf.Max(0.05f, value); }
    public float AttackRange    => attackRange;

    // ── Detection ─────────────────────────────────────────────────────────────
    [Header("Detection")]
    [SerializeField] private float detectionRange    = 5f;
    [SerializeField] private bool  canSeeThoughWalls = false;
    [SerializeField] private float spottedDuration   = 1f;
    [SerializeField] private float lostDuration      = 2f;

    public float DetectionRange    { get => detectionRange;    set => detectionRange    = Mathf.Max(0f, value); }
    public bool  CanSeeThoughWalls { get => canSeeThoughWalls; set => canSeeThoughWalls = value; }

    // ── Wander ────────────────────────────────────────────────────────────────
    [Header("Wander")]
    [SerializeField] private float minWanderTime = 1.5f;
    [SerializeField] private float maxWanderTime = 4.0f;
    [SerializeField] private float minPauseTime  = 0.5f;
    [SerializeField] private float maxPauseTime  = 2.0f;
    [SerializeField] private float wanderRadius  = 6f;

    // ── Animation ─────────────────────────────────────────────────────────────
    [Header("Animation Prefixes")]
    [SerializeField] private string walkPrefix   = "walk";
    [SerializeField] private string attackPrefix = "attack";

    public string WalkPrefix   { get => walkPrefix;   set => walkPrefix   = value; }
    public string AttackPrefix { get => attackPrefix; set => attackPrefix = value; }

    // ── Death Effect ──────────────────────────────────────────────────────────
    // Sprites are loaded automatically from Resources/Dead/DeadSpritesheet — no Inspector setup needed.
    private static Sprite[] _deathFrames;

    // ── Rotation Sprites ──────────────────────────────────────────────────────
    [Header("Rotation Sprites")]
    [SerializeField] protected Sprite[] rotationSprites = new Sprite[8];

    protected static readonly string[] DirOrder =
        { "north", "north_east", "east", "south_east", "south", "south_west", "west", "north_west" };
    private static readonly string[] RotationDirFiles =
        { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

    // ── Health Bar ────────────────────────────────────────────────────────────
    [Header("Health Bar")]
    [Tooltip("Bar colour override. Leave fully transparent (default) to use the green→yellow→red health gradient. Set for elemental/special enemies.")]
    [SerializeField] private Color barColor = Color.clear;

    public Color BarColor => barColor;

    // ── Icons ─────────────────────────────────────────────────────────────────
    [Header("Icons")]
    [SerializeField] private GameObject exclamationIconPrefab;
    [SerializeField] private GameObject questionIconPrefab;
    [SerializeField] private float iconHeightOffset = 1.2f;

    // ── References ────────────────────────────────────────────────────────────
    protected Rigidbody2D        rb;
    protected Animator           animator;
    protected Health             health;
    protected Transform          player;
    protected Health             playerHealth;
    protected Seeker             seeker;
    protected SpriteRenderer     sr;
    protected PlayerHitEffect    playerHitEffect;
    protected PlayerStatusEffects playerStatusEffects;

    protected float   attackTimer;
    protected Vector2 currentDir = Vector2.down;
    protected string  currentClip = "";

    public Health HealthComponent => health;
    public bool   IsDead          => health != null && health.IsDead;

    // ── Detection State Machine ───────────────────────────────────────────────
    protected enum DetectionState { Wander, Spotted, Active, Lost }
    protected DetectionState detectionState = DetectionState.Wander;

    private enum WanderSubState { Moving, Pausing }
    private WanderSubState wanderSubState;
    private float   wanderTimer;
    private Vector2 wanderTarget;
    protected Vector2 WanderTarget => wanderTarget;

    private float stateTimer;
    private float lookAroundTimer;
    private int   lookAroundStep;
    private const float LookAroundInterval = 0.35f;

    private GameObject activeIcon;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    protected virtual void Awake()
    {
        rb       = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        health   = GetComponent<Health>();
        seeker   = GetComponent<Seeker>();
        sr       = GetComponent<SpriteRenderer>();

        // Auto-attach health bar and push the serialized barColor directly (avoids abstract-type GetComponent timing issues)
        if (GetComponent<EnemyHealthBar>() == null)
            gameObject.AddComponent<EnemyHealthBar>().SetThemeColor(barColor);
    }

    protected virtual void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player              = playerObj.transform;
            playerHealth        = playerObj.GetComponent<Health>();
            playerHitEffect     = playerObj.GetComponent<PlayerHitEffect>();
            playerStatusEffects = playerObj.GetComponent<PlayerStatusEffects>()
                               ?? playerObj.AddComponent<PlayerStatusEffects>();
        }

        health.OnDeath.AddListener(OnDeath);
        LoadRotationSprites();
        EnsureSafeSpawnDistance();
        EnterWander();
    }

    // ── Update Loop ───────────────────────────────────────────────────────────
    private void Update()
    {
        if (health.IsDead) return;
        switch (detectionState)
        {
            case DetectionState.Wander:  UpdateWander();      break;
            case DetectionState.Spotted: UpdateSpotted();     break;
            case DetectionState.Active:  UpdateActiveState(); break;
            case DetectionState.Lost:    UpdateLost();        break;
        }
    }

    private void FixedUpdate()
    {
        if (health.IsDead) return;
        switch (detectionState)
        {
            case DetectionState.Wander: FixedUpdateWander(); break;
            case DetectionState.Active: FixedUpdateActive(); break;
        }
    }

    // ── Wander State ──────────────────────────────────────────────────────────
    private void EnterWander()
    {
        detectionState = DetectionState.Wander;
        wanderSubState = WanderSubState.Moving;
        wanderTimer    = Random.Range(minWanderTime, maxWanderTime);
        wanderTarget   = PickWanderDestination();
        OnWanderDestinationSet(wanderTarget);

        currentDir = (wanderTarget - rb.position).sqrMagnitude > 0.01f
            ? (wanderTarget - rb.position).normalized : Vector2.down;

        animator.enabled = true;
        currentClip = "";
        PlayClip($"{walkPrefix}_{GetDirectionKey(currentDir)}");
    }

    private void UpdateWander()
    {
        if (CanDetectPlayer())
        {
            EnterSpotted();
            return;
        }

        wanderTimer -= Time.deltaTime;

        if (wanderSubState == WanderSubState.Moving)
        {
            bool arrived = Vector2.Distance(rb.position, wanderTarget) < 0.35f;
            if (wanderTimer <= 0f || arrived || PathComplete)
            {
                wanderSubState   = WanderSubState.Pausing;
                wanderTimer      = Random.Range(minPauseTime, maxPauseTime);
                animator.enabled = false;
                UpdateDirectionSprite(GetDirectionKey(currentDir));
            }
        }
        else
        {
            if (wanderTimer <= 0f)
            {
                wanderSubState   = WanderSubState.Moving;
                wanderTimer      = Random.Range(minWanderTime, maxWanderTime);
                wanderTarget     = PickWanderDestination();
                OnWanderDestinationSet(wanderTarget);
                animator.enabled = true;
                currentClip      = "";
                PlayClip($"{walkPrefix}_{GetDirectionKey(currentDir)}");
            }
        }
    }

    private void FixedUpdateWander()
    {
        if (wanderSubState != WanderSubState.Moving) return;

        Vector2 dir = GetWanderMoveDirection();
        if (dir == Vector2.zero) return;

        Vector2 finalDir = (dir + GetSeparationForce()).normalized;
        rb.MovePosition(rb.position + finalDir * MoveSpeed * Time.fixedDeltaTime);

        if (Vector2.Angle(currentDir, finalDir) > 22.5f)
        {
            currentDir = finalDir;
            PlayClip($"{walkPrefix}_{GetDirectionKey(currentDir)}");
        }
    }

    // Override in Ghost: direct wall-phasing movement direction
    protected virtual Vector2 GetWanderMoveDirection()
    {
        Vector2 dir = GetNextPathDirection();
        if (dir == Vector2.zero) dir = LastPathDir;
        return dir;
    }

    // Override in Ghost: skip StartPathTo (moves directly, no path needed)
    protected virtual void OnWanderDestinationSet(Vector2 destination)
    {
        StartPathTo(destination);
    }

    protected virtual Vector2 PickWanderDestination()
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

    // ── Spotted State ─────────────────────────────────────────────────────────
    private void EnterSpotted()
    {
        detectionState   = DetectionState.Spotted;
        stateTimer       = spottedDuration;
        currentDir       = DirectionToPlayer();
        animator.enabled = false;
        UpdateDirectionSprite(GetDirectionKey(currentDir));
        SpawnIcon(exclamationIconPrefab, spottedDuration, false);
    }

    private void UpdateSpotted()
    {
        stateTimer -= Time.deltaTime;
        currentDir  = DirectionToPlayer();
        UpdateDirectionSprite(GetDirectionKey(currentDir));
        if (stateTimer <= 0f) EnterActive();
    }

    // ── Active State ──────────────────────────────────────────────────────────
    private void EnterActive()
    {
        detectionState   = DetectionState.Active;
        attackTimer      = AttackCooldown;
        animator.enabled = true;
        currentClip      = "";
        OnActivated();
    }

    private void UpdateActiveState()
    {
        attackTimer += Time.deltaTime;
        if (!CanDetectPlayer(extendedRange: true))
        {
            EnterLost();
            return;
        }
        UpdateActive();
    }

    protected virtual void UpdateActive()       {}
    protected virtual void FixedUpdateActive()  {}
    protected virtual void OnActivated()        {}
    protected virtual void OnDeactivated()      {}

    // ── Lost State ────────────────────────────────────────────────────────────
    private void EnterLost()
    {
        detectionState   = DetectionState.Lost;
        stateTimer       = lostDuration;
        lookAroundTimer  = 0f;
        lookAroundStep   = 0;
        animator.enabled = false;
        UpdateDirectionSprite(GetDirectionKey(currentDir));
        SpawnIcon(questionIconPrefab, lostDuration, true);
        OnDeactivated();
    }

    private void UpdateLost()
    {
        if (CanDetectPlayer())
        {
            EnterSpotted();
            return;
        }

        stateTimer      -= Time.deltaTime;
        lookAroundTimer -= Time.deltaTime;

        if (lookAroundTimer <= 0f)
        {
            lookAroundTimer = LookAroundInterval;
            DoLookAroundStep();
        }

        if (stateTimer <= 0f)
        {
            animator.enabled = true;
            currentClip      = "";
            EnterWander();
        }
    }

    private void DoLookAroundStep()
    {
        int baseIdx = System.Array.IndexOf(DirOrder, GetDirectionKey(currentDir));
        if (baseIdx < 0) return;

        // Sequence: centre → left → centre → right → centre → ...
        string dirKey;
        switch (lookAroundStep % 4)
        {
            case 1:  dirKey = DirOrder[(baseIdx + 7) % 8]; break; // left-adjacent
            case 3:  dirKey = DirOrder[(baseIdx + 1) % 8]; break; // right-adjacent
            default: dirKey = DirOrder[baseIdx];            break; // centre
        }
        lookAroundStep++;
        UpdateDirectionSprite(dirKey);
    }

    // ── Detection Check ───────────────────────────────────────────────────────
    private bool CanDetectPlayer(bool extendedRange = false)
    {
        if (player == null || playerHealth == null || playerHealth.IsDead) return false;

        float range = extendedRange ? detectionRange * 1.5f : detectionRange;
        if (DistanceToPlayer() > range) return false;
        if (canSeeThoughWalls) return true;

        Vector2 dir      = DirectionToPlayer();
        float   dist     = DistanceToPlayer();
        int     wallMask = LayerMask.GetMask("Walls");
        // Stop 0.45 units short of the player so a player pressed against a wall
        // doesn't cause the wall collider to occlude the ray end-point.
        float checkDist = Mathf.Max(0f, dist - 1.1f);
        return !Physics2D.Raycast(rb.position, dir, checkDist, wallMask);
    }

    // ── Sprite Helpers ────────────────────────────────────────────────────────
    protected void UpdateDirectionSprite(string dirKey)
    {
        if (sr == null || rotationSprites == null || rotationSprites.Length < 8) return;
        int idx = System.Array.IndexOf(DirOrder, dirKey);
        if (idx >= 0 && rotationSprites[idx] != null)
            sr.sprite = rotationSprites[idx];
    }

    protected void PlayClip(string clip)
    {
        if (clip == currentClip) return;
        currentClip = clip;
        animator.Play(clip);
    }

    // ── Icons ─────────────────────────────────────────────────────────────────
    private void SpawnIcon(GameObject prefab, float duration, bool shake)
    {
        if (activeIcon != null) Destroy(activeIcon);
        if (prefab == null) return;

        activeIcon = Instantiate(prefab);
        activeIcon.transform.localScale = Vector3.zero;

        if (shake) StartCoroutine(AnimateShake(activeIcon, duration));
        else       StartCoroutine(AnimatePopIn(activeIcon, duration));
    }

    private IEnumerator AnimatePopIn(GameObject icon, float duration)
    {
        float riseTime   = duration * 0.2f;
        float settleTime = duration * 0.1f;
        float holdTime   = duration * 0.5f;
        float shrinkTime = duration * 0.2f;

        for (float t = 0f; t < riseTime; t += Time.deltaTime)
        {
            if (icon == null) yield break;
            icon.transform.position   = transform.position + Vector3.up * iconHeightOffset;
            icon.transform.localScale = Vector3.one * Mathf.Lerp(0f, 1.2f, t / riseTime);
            yield return null;
        }
        for (float t = 0f; t < settleTime; t += Time.deltaTime)
        {
            if (icon == null) yield break;
            icon.transform.position   = transform.position + Vector3.up * iconHeightOffset;
            icon.transform.localScale = Vector3.one * Mathf.Lerp(1.2f, 1.0f, t / settleTime);
            yield return null;
        }

        float holdEnd = Time.time + holdTime;
        while (Time.time < holdEnd)
        {
            if (icon == null) yield break;
            icon.transform.position = transform.position + Vector3.up * iconHeightOffset;
            yield return null;
        }

        for (float t = 0f; t < shrinkTime; t += Time.deltaTime)
        {
            if (icon == null) yield break;
            icon.transform.position   = transform.position + Vector3.up * iconHeightOffset;
            icon.transform.localScale = Vector3.one * Mathf.Lerp(1.0f, 0f, t / shrinkTime);
            yield return null;
        }
        if (icon != null) Destroy(icon);
    }

    private IEnumerator AnimateShake(GameObject icon, float duration)
    {
        if (icon != null) icon.transform.localScale = Vector3.one;

        float   elapsed  = 0f;
        int     step     = 0;
        float[] angles   = { 0f, 30f, 0f, -30f };

        while (elapsed < duration)
        {
            if (icon == null) yield break;
            icon.transform.position   = transform.position + Vector3.up * iconHeightOffset;
            icon.transform.rotation   = Quaternion.Euler(0f, 0f, angles[step % 4]);
            step++;
            yield return new WaitForSeconds(0.25f);
            elapsed += 0.25f;
        }
        if (icon != null) Destroy(icon);
    }

    // ── Shared Helpers ────────────────────────────────────────────────────────
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
        if (angle >= 337.5f || angle < 22.5f) return "east";
        if (angle < 67.5f)                    return "north_east";
        if (angle < 112.5f)                   return "north";
        if (angle < 157.5f)                   return "north_west";
        if (angle < 202.5f)                   return "west";
        if (angle < 247.5f)                   return "south_west";
        if (angle < 292.5f)                   return "south";
        return                                       "south_east";
    }

    protected abstract void Attack();

    protected virtual void OnDeath()
    {
        // Disable all colliders immediately — dead bodies shouldn't block movement or projectiles
        foreach (var col in GetComponentsInChildren<Collider2D>())
            col.enabled = false;

        // Freeze the rigidbody so the corpse doesn't drift
        if (rb != null)
            rb.constraints = RigidbodyConstraints2D.FreezeAll;

        // Stop animator and shadow immediately
        if (animator != null) animator.enabled = false;
        var shadow = GetComponent<SpriteShadow>();
        if (shadow != null)
        {
            shadow.enabled = false;
            transform.Find("_Shadow")?.gameObject.SetActive(false);
        }

        // Lazy-load death frames once from Resources/Dead/DeadSpritesheet
        if (_deathFrames == null || _deathFrames.Length == 0)
            _deathFrames = Resources.LoadAll<Sprite>("Dead/DeadSpritesheet");

        if (_deathFrames == null || _deathFrames.Length == 0)
            Debug.LogWarning("[EnemyBase] Death frames not found. Make sure DeadSpritesheet.png is at Assets/Resources/Dead/DeadSpritesheet.png and is set to Multiple sprite mode with sliced frames.");

        // Dissolve the enemy's own sprite in-place over 2 s
        if (sr != null) StartCoroutine(DissolveSprite(sr));

        // Spawn the floating ghost effect simultaneously
        EnemyDeathEffect.Spawn(transform.position, _deathFrames,
                               sr != null ? sr.sortingLayerName : "Entities",
                               sr != null ? sr.sortingOrder     : 0);

        if (activeIcon != null) Destroy(activeIcon);
        Destroy(gameObject, 2f);
    }

    private static IEnumerator DissolveSprite(SpriteRenderer target)
    {
        var shader = Shader.Find("Custom/EnemyDissolve");
        if (shader == null) yield break;

        var mat = new Material(shader);
        mat.SetTexture("_NoiseTex", EnemyDeathEffect.GetNoiseTexture());
        mat.SetFloat("_Dissolve", 0f);
        target.sharedMaterial = mat;

        const float Duration = 2f;
        float elapsed = 0f;
        while (elapsed < Duration && target != null)
        {
            mat.SetFloat("_Dissolve", elapsed / Duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // ── Separation ────────────────────────────────────────────────────────────
    [Header("Separation")]
    [SerializeField] private float separationRadius = 1.2f;
    [SerializeField] [Range(0f, 2f)] private float separationWeight = 0.8f;
    private static readonly Collider2D[] SeparationBuffer = new Collider2D[16];

    protected Vector2 GetSeparationForce()
    {
        Vector2 force = Vector2.zero;
        int count = Physics2D.OverlapCircleNonAlloc(rb.position, separationRadius, SeparationBuffer);
        for (int i = 0; i < count; i++)
        {
            if (SeparationBuffer[i].gameObject == gameObject) continue;
            if (SeparationBuffer[i].GetComponent<EnemyBase>() == null) continue;
            Vector2 away = rb.position - (Vector2)SeparationBuffer[i].transform.position;
            float dist = away.magnitude;
            if (dist > 0.01f)
                force += away.normalized * (1f - dist / separationRadius);
        }
        return force * separationWeight;
    }

    // ── Pathfinding ───────────────────────────────────────────────────────────
    private List<Vector3> _pathWaypoints;
    private int           _waypointIndex;
    private bool          _pathReady;
    private Vector2       _lastPathDir;
    private const float   WaypointReachedDist = 0.2f;

    protected bool    PathComplete => _pathReady && _waypointIndex >= (_pathWaypoints?.Count ?? 0);
    protected bool    HasPath      => _pathReady && _pathWaypoints != null && _pathWaypoints.Count > 0;
    protected Vector2 LastPathDir  => _lastPathDir;

    protected void StartPathTo(Vector2 target)
    {
        if (seeker == null || AstarPath.active == null) return;
        _pathReady = false;
        seeker.StartPath(rb.position, target, p =>
        {
            if (p.error) return;
            _pathWaypoints = p.vectorPath;
            _waypointIndex = 0;
            _pathReady     = true;
        });
    }

    protected Vector2 GetNextPathDirection()
    {
        if (!HasPath) return Vector2.zero;
        while (_waypointIndex < _pathWaypoints.Count &&
               Vector2.Distance(rb.position, _pathWaypoints[_waypointIndex]) < WaypointReachedDist)
        {
            _waypointIndex++;
        }
        if (_waypointIndex >= _pathWaypoints.Count) return Vector2.zero;
        _lastPathDir = ((Vector2)_pathWaypoints[_waypointIndex] - rb.position).normalized;
        return _lastPathDir;
    }

    // ── Spawn Safety ──────────────────────────────────────────────────────────
    private void EnsureSafeSpawnDistance()
    {
        if (player == null) return;
        float safeDistance = 2.5f;
        if (DistanceToPlayer() < safeDistance)
        {
            Vector2 away   = -DirectionToPlayer();
            Vector2 newPos = (Vector2)player.position + away * safeDistance;
            rb.position        = newPos;
            transform.position = newPos;
            Physics2D.SyncTransforms();
        }
    }

    // ── Rotation Sprites ──────────────────────────────────────────────────────
    protected void LoadRotationSprites()
    {
        if (rotationSprites != null && rotationSprites.Length == 8 && rotationSprites[0] != null) return;
#if UNITY_EDITOR
        string enemyName = gameObject.name.Replace("(Clone)", "").Replace(" Variant", "").Trim();
        rotationSprites = new Sprite[8];
        for (int i = 0; i < 8; i++)
        {
            string path = $"Assets/Art/Sprites/Enemies/{enemyName}/rotations/{RotationDirFiles[i]}.png";
            rotationSprites[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
#endif
    }

#if UNITY_EDITOR
    [ContextMenu("Bake Rotation Sprites")]
    private void BakeRotationSprites()
    {
        string enemyName = gameObject.name.Replace("(Clone)", "").Replace(" Variant", "").Trim();
        rotationSprites  = new Sprite[8];
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
