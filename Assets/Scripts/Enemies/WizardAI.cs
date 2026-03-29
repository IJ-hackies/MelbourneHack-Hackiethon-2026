using UnityEngine;

// Shared AI for ranged wizard enemies — launches a physics projectile on attack.
// Damage is dealt only if the projectile collides with the Player (dodgeable).
// Used by FireWizard, IceWizard, and any future wizard type.
public class WizardAI : MeleeChaseAI
{
    [Header("Projectile")]
    [SerializeField] private Color          projectileColorA = new Color(1.0f, 0.35f, 0.0f, 1f);
    [SerializeField] private Color          projectileColorB = new Color(1.0f, 0.85f, 0.1f, 1f);
    [SerializeField] private float          projectileSpeed  = 7f;
    [SerializeField] private ProjectileStyle projectileStyle  = ProjectileStyle.Fire;

    [Header("Slow on Hit (0 = disabled)")]
    [SerializeField] private float slowMultiplier = 0f;
    [SerializeField] private float slowDuration   = 0f;

    [Header("Homing")]
    [SerializeField] private float homingStrength    = 2.5f; // radians/sec turn rate
    [SerializeField] private float stopHomingRadius  = 2.5f; // locks direction inside this distance

    [Header("Burn on Hit (0 = disabled)")]
    [SerializeField] private float burnDamagePercent = 0f;   // fraction of AttackDamage per tick
    [SerializeField] private float burnDuration      = 0f;

    public Color ProjectileColorA  { get => projectileColorA;  set => projectileColorA  = value; }
    public Color ProjectileColorB  { get => projectileColorB;  set => projectileColorB  = value; }
    public float ProjectileSpeed   { get => projectileSpeed;   set => projectileSpeed   = Mathf.Max(1f, value); }
    public float SlowMultiplier    { get => slowMultiplier;    set => slowMultiplier    = Mathf.Clamp01(value); }
    public float SlowDuration      { get => slowDuration;      set => slowDuration      = Mathf.Max(0f, value); }
    public float HomingStrength    { get => homingStrength;    set => homingStrength    = Mathf.Max(0f, value); }
    public float StopHomingRadius  { get => stopHomingRadius;  set => stopHomingRadius  = Mathf.Max(0f, value); }
    public float BurnDamagePercent { get => burnDamagePercent; set => burnDamagePercent = Mathf.Max(0f, value); }
    public float BurnDuration      { get => burnDuration;      set => burnDuration      = Mathf.Max(0f, value); }


    protected override void Start()
    {
        base.Start();
        CanSeeThoughWalls = true;
    }

    protected override void Attack()
    {
        if (playerHealth == null || playerHealth.IsDead) return;

        Vector2 dir      = DirectionToPlayer();
        Vector3 spawnPos = transform.position + (Vector3)(dir * 0.45f);
        FireballProjectile.Spawn(spawnPos, dir, AttackDamage, projectileColorA, projectileColorB,
                                 playerHealth, playerHitEffect, projectileSpeed,
                                 slowMultiplier: slowMultiplier, slowDuration: slowDuration,
                                 playerTransform: player,
                                 homingStrength: homingStrength, stopHomingRadius: stopHomingRadius,
                                 burnDamagePerTick: AttackDamage * burnDamagePercent,
                                 burnDuration: burnDuration,
                                 pierceWalls: true,
                                 style: projectileStyle);
    }
}
