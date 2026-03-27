using UnityEngine;

// Shared AI for ranged wizard enemies — launches a physics projectile on attack.
// Damage is dealt only if the projectile collides with the Player (dodgeable).
// Used by FireWizard, IceWizard, and any future wizard type.
public class WizardAI : MeleeChaseAI
{
    [Header("Projectile")]
    [SerializeField] private Color projectileColorA = new Color(1.0f, 0.35f, 0.0f, 1f);
    [SerializeField] private Color projectileColorB = new Color(1.0f, 0.85f, 0.1f, 1f);
    [SerializeField] private float projectileSpeed  = 7f;

    public Color ProjectileColorA { get => projectileColorA; set => projectileColorA = value; }
    public Color ProjectileColorB { get => projectileColorB; set => projectileColorB = value; }
    public float ProjectileSpeed  { get => projectileSpeed;  set => projectileSpeed  = Mathf.Max(1f, value); }

    private PlayerHitEffect playerHitEffect;

    protected override void Start()
    {
        base.Start();
        if (player != null)
            playerHitEffect = player.GetComponent<PlayerHitEffect>();
    }

    protected override void Attack()
    {
        if (playerHealth == null || playerHealth.IsDead) return;

        Vector2 dir      = DirectionToPlayer();
        Vector3 spawnPos = transform.position + (Vector3)(dir * 0.45f);
        FireballProjectile.Spawn(spawnPos, dir, AttackDamage, projectileColorA, projectileColorB,
                                 playerHealth, playerHitEffect, projectileSpeed);
    }
}
