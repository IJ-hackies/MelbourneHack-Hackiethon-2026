using UnityEngine;

// Physics fireball spawned by WizardAI.
// Homes toward the player by gradually rotating its velocity (RotateTowards).
// Once within stopHomingRadius the direction locks and it flies straight from there.
// Damage is only dealt on actual collider contact — the player can dodge it.
public class FireballProjectile : MonoBehaviour
{
    private float               damage;
    private Color               colorA;
    private Color               colorB;
    private Health              playerHealth;
    private PlayerHitEffect     playerHitEffect;
    private float               slowMultiplier;
    private float               slowDuration;
    private float               burnDamagePerTick;
    private float               burnDuration;
    private float               poisonDuration;

    private Rigidbody2D         rb;
    private Transform           playerTransform;
    private float               speed;
    private float               homingStrength;   // radians/sec turn rate
    private float               stopHomingRadius; // world units — locks direction inside this
    private bool                homingLocked;
    private float               age;

    private const int WallLayerMask = 1 << 9; // Walls layer

    public static void Spawn(Vector3 from, Vector2 dir, float damage,
                             Color colorA, Color colorB,
                             Health playerHealth, PlayerHitEffect playerHitEffect,
                             float speed = 7f, float maxLife = 4f,
                             float slowMultiplier = 0f, float slowDuration = 0f,
                             Transform playerTransform = null,
                             float homingStrength = 2.5f, float stopHomingRadius = 2.5f,
                             float burnDamagePerTick = 0f, float burnDuration = 0f,
                             float poisonDuration = 0f)
    {
        var go = new GameObject("FX_Fireball");
        go.transform.position = from;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale           = 0f;
        rb.linearVelocity         = dir.normalized * speed;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var trigger       = go.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius    = 0.15f;

        var proj                 = go.AddComponent<FireballProjectile>();
        proj.rb                  = rb;
        proj.damage              = damage;
        proj.colorA              = colorA;
        proj.colorB              = colorB;
        proj.playerHealth        = playerHealth;
        proj.playerHitEffect     = playerHitEffect;
        proj.slowMultiplier      = slowMultiplier;
        proj.slowDuration        = slowDuration;
        proj.burnDamagePerTick = burnDamagePerTick;
        proj.burnDuration      = burnDuration;
        proj.poisonDuration    = poisonDuration;
        proj.playerTransform     = playerTransform;
        proj.speed               = speed;
        proj.homingStrength      = homingStrength;
        proj.stopHomingRadius    = stopHomingRadius;

        proj.SetupParticles(colorA, colorB);
        HitEffectSpawner.AddGlowSprite(go.transform, colorA, 0.3f, 99);
        HitEffectSpawner.AddTrailRenderer(go, colorA, colorB, trailTime: 0.15f, startWidth: 0.22f);
        Destroy(go, maxLife);
    }

    private void FixedUpdate()
    {
        age += Time.fixedDeltaTime;

        Vector2 nextPos = rb.position + rb.linearVelocity * Time.fixedDeltaTime * 2f;
        if (Physics2D.Linecast(rb.position, nextPos, WallLayerMask).collider != null)
        {
            Destroy(gameObject);
            return;
        }

        if (homingLocked || playerTransform == null) return;

        float dist = Vector2.Distance(rb.position, (Vector2)playerTransform.position);

        if (dist <= stopHomingRadius)
        {
            homingLocked = true; // within range — fly straight from here
            return;
        }

        Vector2 toPlayer   = ((Vector2)playerTransform.position - rb.position).normalized;
        Vector3 currentDir = rb.linearVelocity.normalized;
        Vector3 newDir     = Vector3.RotateTowards(currentDir,
                                 new Vector3(toPlayer.x, toPlayer.y, 0f),
                                 homingStrength * Time.fixedDeltaTime, 0f);

        rb.linearVelocity = new Vector2(newDir.x, newDir.y) * speed;
    }

    private void SetupParticles(Color colorA, Color colorB)
    {
        var child = new GameObject("FX");
        child.transform.SetParent(transform, false);

        var ps = child.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr      = child.GetComponent<ParticleSystemRenderer>();
        psr.material = HitEffectSpawner.GetAdditiveParticleMaterial();

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = 0.15f;
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0f, 1f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        main.startColor      = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = 50f;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.08f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(colorA, 0f), new GradientColorKey(colorB, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        // Set sorting after all PS configuration so it isn't overwritten
        psr.sortingLayerID = SortingLayer.NameToID("Entities");
        psr.sortingOrder   = 100;
        ps.Play();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (age < 0.05f) return;
        if (other.gameObject.layer != LayerMask.NameToLayer("Hitbox")) return;
        if (playerHealth == null || playerHealth.IsDead) return;

        playerHealth.TakeDamage(damage);
        HitEffectSpawner.SpawnHit(transform.position, colorA, colorB);
        HitEffectSpawner.SpawnImpactFlash(transform.position, colorA, colorB);
        playerHitEffect?.PlayHitEffect();

        var statusEffects = other.GetComponentInParent<PlayerStatusEffects>();
        if (statusEffects == null) return;

        if (slowMultiplier > 0f)
            statusEffects.ApplyTimedSlow(slowMultiplier, slowDuration);

        if (burnDamagePerTick > 0f)
            statusEffects.ApplyBurn(burnDamagePerTick, burnDuration);

        if (poisonDuration > 0f)
            statusEffects.ApplyPoison(poisonDuration);

        Destroy(gameObject);
    }
}
