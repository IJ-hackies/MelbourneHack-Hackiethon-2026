using UnityEngine;

// A single shockwave shard — moves outward from an impact point and damages the player on contact.
// Spawned in a ring by DragonNewtAI via ShockwaveProjectile.SpawnRing().
public class ShockwaveProjectile : MonoBehaviour
{
    private float           damage;
    private Color           colorA;
    private Color           colorB;
    private Health          playerHealth;
    private PlayerHitEffect playerHitEffect;
    private bool            hit;

    // Spawns `count` projectiles evenly distributed in a full circle around `origin`.
    public static void SpawnRing(Vector3 origin, int count, float speed, float maxLife,
                                 float damage, Color colorA, Color colorB,
                                 Health playerHealth, PlayerHitEffect playerHitEffect)
    {
        float step = 360f / count;
        for (int i = 0; i < count; i++)
        {
            float   angle = i * step * Mathf.Deg2Rad;
            Vector2 dir   = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Spawn(origin, dir, speed, maxLife, damage, colorA, colorB, playerHealth, playerHitEffect);
        }
    }

    private static void Spawn(Vector3 origin, Vector2 dir, float speed, float maxLife,
                               float damage, Color colorA, Color colorB,
                               Health playerHealth, PlayerHitEffect playerHitEffect)
    {
        var go = new GameObject("FX_Shockwave");
        go.transform.position = origin;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale           = 0f;
        rb.linearVelocity         = dir * speed;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var col      = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius    = 0.22f;

        var proj             = go.AddComponent<ShockwaveProjectile>();
        proj.damage          = damage;
        proj.colorA          = colorA;
        proj.colorB          = colorB;
        proj.playerHealth    = playerHealth;
        proj.playerHitEffect = playerHitEffect;

        proj.SetupParticles(colorA, colorB);
        HitEffectSpawner.AddGlowSprite(go.transform, colorA, 0.22f, 99);
        HitEffectSpawner.AddTrailRenderer(go, colorA, colorB, trailTime: 0.08f, startWidth: 0.15f);
        Destroy(go, maxLife);
    }

    private void SetupParticles(Color colorA, Color colorB)
    {
        var child = new GameObject("FX");
        child.transform.SetParent(transform, false);

        var ps = child.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr              = child.GetComponent<ParticleSystemRenderer>();
        psr.material         = HitEffectSpawner.GetAdditiveParticleMaterial();
        psr.sortingLayerName = "Entities";
        psr.sortingOrder     = 100;

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0f, 0.5f); // mostly stationary — trail stays
        main.startSize       = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
        main.startColor      = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.simulationSpace = ParticleSystemSimulationSpace.World; // particles stay behind = trail
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = 40f;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.08f;

        var col2 = ps.colorOverLifetime;
        col2.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(colorA, 0f), new GradientColorKey(colorB, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col2.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hit || other.gameObject.layer != LayerMask.NameToLayer("Hitbox")) return;
        if (playerHealth == null || playerHealth.IsDead) return;

        hit = true;
        playerHealth.TakeDamage(damage);
        HitEffectSpawner.SpawnHit(transform.position, colorA, colorB);
        HitEffectSpawner.SpawnImpactFlash(transform.position, colorA, colorB);
        playerHitEffect?.PlayHitEffect();
        Destroy(gameObject);
    }
}
