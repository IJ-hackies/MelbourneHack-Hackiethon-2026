using System.Collections;
using UnityEngine;

// Vampire AI — extends AlienAI (random wander + ranged attack).
// Telegraph and anim duration are derived from AttackCooldown; neither
// is settable in the Inspector.
public class VampireAI : AlienAI
{
    // Telegraph takes 40% of the cooldown window — scales if AttackCooldown changes at runtime.
    private float TelegraphDuration => AttackCooldown * 0.55f;

    protected override void Start()
    {
        base.Start();
        CanSeeThoughWalls = true;
        DamageHitFrame = 0.01f; // fire Attack() almost immediately so animator freezes on frame 0
    }

    protected override void Attack()
    {
        if (playerHealth == null || playerHealth.IsDead || player == null) return;

        // Recompute every attack so runtime cooldown changes are picked up
        AttackAnimDuration = TelegraphDuration
                           + BloodBeamProjectile.LockOnDuration
                           + BloodBeamProjectile.FireDuration;

        Vector2 initialDir = DirectionToPlayer();
        BloodBeamProjectile.Spawn(transform, initialDir, AttackDamage,
                                  playerHealth,
                                  player.GetComponent<PlayerHitEffect>(),
                                  player,
                                  TelegraphDuration);

        StartCoroutine(TrackAndFire());
    }

    private IEnumerator TrackAndFire()
    {
        // Telegraph phase: freeze on frame 0, update directional clip each frame to track player
        animator.speed = 0f;
        string activeClip = "";

        for (float t = 0f; t < TelegraphDuration; t += Time.deltaTime)
        {
            string clip = $"{AttackPrefix}_{GetDirectionKey(DirectionToPlayer())}";
            if (clip != activeClip)
            {
                activeClip = clip;
                animator.Play(clip, 0, 0f); // jump to frame 0 of the correct directional clip
            }
            yield return null;
        }

        // Lock-on phase: unfreeze so the fireball animation plays BEFORE the beam fires,
        // making the shot look like it causes the beam rather than reacting to it.
        animator.speed = 1f;
    }
}
