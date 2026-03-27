using UnityEngine;

// Physics fireball spawned by FireWizardAI.
// Travels via Rigidbody2D — only deals damage if it actually hits the Player collider.
// Visual is a particle system riding the same GameObject.
public class FireballProjectile : MonoBehaviour
{
    private float           damage;
    private Color           colorA;
    private Color           colorB;
    private Health          playerHealth;
    private PlayerHitEffect playerHitEffect;

    public static void Spawn(Vector3 from, Vector2 dir, float damage,
                             Color colorA, Color colorB,
                             Health playerHealth, PlayerHitEffect playerHitEffect,
                             float speed = 7f, float maxLife = 4f)
    {
        var go = new GameObject("FX_Fireball");
        go.transform.position = from;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale           = 0f;
        rb.linearVelocity         = dir.normalized * speed;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var trigger      = go.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius    = 0.15f;

        var proj             = go.AddComponent<FireballProjectile>();
        proj.damage          = damage;
        proj.colorA          = colorA;
        proj.colorB          = colorB;
        proj.playerHealth    = playerHealth;
        proj.playerHitEffect = playerHitEffect;

        proj.SetupParticles(colorA, colorB);
        Destroy(go, maxLife);
    }

    private void SetupParticles(Color colorA, Color colorB)
    {
        var ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        GetComponent<ParticleSystemRenderer>().material = new Material(urp != null ? urp : fallback);

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = 0.15f;
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0f, 1f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        main.startColor      = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.simulationSpace = ParticleSystemSimulationSpace.World; // particles stay behind = trail
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

        ps.Play();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (playerHealth == null || playerHealth.IsDead) return;

        playerHealth.TakeDamage(damage);
        HitEffectSpawner.SpawnHit(transform.position, colorA, colorB);
        playerHitEffect?.PlayHitEffect();
        Destroy(gameObject);
    }
}
