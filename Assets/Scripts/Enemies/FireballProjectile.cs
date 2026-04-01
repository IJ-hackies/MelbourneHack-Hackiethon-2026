using UnityEngine;
using UnityEngine.Rendering.Universal;

public enum ProjectileStyle { Fire, Ice, Alien }

// Physics fireball spawned by WizardAI (fire/ice) and AlienAI.
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
    private bool                pierceWalls;
    private ProjectileStyle     style;

    private Rigidbody2D         rb;
    private Transform           playerTransform;
    private float               speed;
    private float               homingStrength;
    private float               stopHomingRadius;
    private bool                homingLocked;
    private float               age;

    // Pulse ring (animates in Update)
    private Transform           pulseRingTransform;
    private SpriteRenderer      pulseRingSr;
    private float               pulseTimer;
    private float               pulseCycleDuration;
    private float               pulseMaxSize;

    // Dynamic light
    private Light2D             projLight;

    private const int WallLayerMask = 1 << 9; // Walls layer

    public static void Spawn(Vector3 from, Vector2 dir, float damage,
                             Color colorA, Color colorB,
                             Health playerHealth, PlayerHitEffect playerHitEffect,
                             float speed = 7f, float maxLife = 4f,
                             float slowMultiplier = 0f, float slowDuration = 0f,
                             Transform playerTransform = null,
                             float homingStrength = 2.5f, float stopHomingRadius = 2.5f,
                             float burnDamagePerTick = 0f, float burnDuration = 0f,
                             float poisonDuration = 0f,
                             bool pierceWalls = false,
                             ProjectileStyle style = ProjectileStyle.Fire)
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
        proj.burnDamagePerTick   = burnDamagePerTick;
        proj.burnDuration        = burnDuration;
        proj.poisonDuration      = poisonDuration;
        proj.pierceWalls         = pierceWalls;
        proj.playerTransform     = playerTransform;
        proj.speed               = speed;
        proj.homingStrength      = homingStrength;
        proj.stopHomingRadius    = stopHomingRadius;
        proj.style               = style;

        proj.SetupVisuals(colorA, colorB, style);
        Destroy(go, maxLife);
    }

    // ── Per-frame animation ───────────────────────────────────────────────────

    private void Update()
    {
        // Pulsing corona ring — expands outward and fades
        if (pulseRingTransform != null && pulseRingSr != null)
        {
            pulseTimer += Time.deltaTime;
            float t = (pulseTimer % pulseCycleDuration) / pulseCycleDuration;
            pulseRingTransform.localScale = Vector3.one * Mathf.Lerp(0.20f, pulseMaxSize, t);
            Color c = pulseRingSr.color;
            pulseRingSr.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.85f, 0f, t));
        }

        if (projLight == null) return;
        switch (style)
        {
            case ProjectileStyle.Fire:
                // Irregular flicker — two overlapping sine waves at different frequencies
                projLight.intensity = 1.8f
                    + Mathf.Sin(Time.time * 23.7f) * 0.50f
                    + Mathf.Sin(Time.time * 11.3f) * 0.25f;
                break;
            case ProjectileStyle.Alien:
                // Slow organic pulse
                projLight.intensity = 1.3f + Mathf.Sin(Time.time * 5.5f) * 0.40f;
                break;
            // Ice: steady — no per-frame update needed
        }
    }

    // ── Physics ───────────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        age += Time.fixedDeltaTime;

        if (!pierceWalls)
        {
            Vector2 nextPos = rb.position + rb.linearVelocity * Time.fixedDeltaTime * 2f;
            if (Physics2D.Linecast(rb.position, nextPos, WallLayerMask).collider != null)
            {
                Destroy(gameObject);
                return;
            }
        }

        if (homingLocked || playerTransform == null) return;

        float dist = Vector2.Distance(rb.position, (Vector2)playerTransform.position);

        if (dist <= stopHomingRadius)
        {
            homingLocked = true;
            return;
        }

        Vector2 toPlayer   = ((Vector2)playerTransform.position - rb.position).normalized;
        Vector3 currentDir = rb.linearVelocity.normalized;
        Vector3 newDir     = Vector3.RotateTowards(currentDir,
                                 new Vector3(toPlayer.x, toPlayer.y, 0f),
                                 homingStrength * Time.fixedDeltaTime, 0f);

        rb.linearVelocity = new Vector2(newDir.x, newDir.y) * speed;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (age < 0.05f) return;
        if (other.gameObject.layer != LayerMask.NameToLayer("Hitbox")) return;
        if (playerHealth == null || playerHealth.IsDead) return;

        EnemyBase.LogDamageToPlayer(playerHealth, damage, "fireball");
        HitEffectSpawner.SpawnHit(transform.position, colorA, colorB);
        HitEffectSpawner.SpawnImpactFlash(transform.position, colorA, colorB);
        playerHitEffect?.PlayHitEffect();

        var statusEffects = other.GetComponentInParent<PlayerStatusEffects>();
        if (statusEffects != null)
        {
            if (slowMultiplier > 0f)    statusEffects.ApplyTimedSlow(slowMultiplier, slowDuration);
            if (burnDamagePerTick > 0f) statusEffects.ApplyBurn(burnDamagePerTick, burnDuration);
            if (poisonDuration > 0f)    statusEffects.ApplyPoison(poisonDuration);
        }

        Destroy(gameObject);
    }

    // ── Visual setup dispatcher ───────────────────────────────────────────────

    private void SetupVisuals(Color colorA, Color colorB, ProjectileStyle style)
    {
        switch (style)
        {
            case ProjectileStyle.Fire:  SetupFireVisuals(colorA, colorB);  break;
            case ProjectileStyle.Ice:   SetupIceVisuals(colorA, colorB);   break;
            case ProjectileStyle.Alien: SetupAlienVisuals(colorA, colorB); break;
        }
    }

    // ── FIRE — dark core, roiling corona, floating embers ────────────────────

    private void SetupFireVisuals(Color colorA, Color colorB)
    {
        // Dense core flame particles
        SetupCoreParticles(colorA, colorB,
            rateOverTime: 100f,
            lifetime:     new ParticleSystem.MinMaxCurve(0.12f, 0.22f),
            speed:        new ParticleSystem.MinMaxCurve(0.5f,  2.5f),
            size:         new ParticleSystem.MinMaxCurve(0.18f, 0.38f));

        // Embers — float upward, linger as small sparks
        SetupEmberParticles(colorA, colorB);

        // Two-layer glow: tight hot core + wide soft aura
        HitEffectSpawner.AddGlowSprite(transform, colorA,                         0.30f, 102);
        HitEffectSpawner.AddGlowSprite(transform, new Color(1f, 0.45f, 0.0f, 1f), 0.70f,  98);

        // Expanding corona ring
        SetupPulseRing(colorA, pulseCycleDuration: 0.35f, maxSize: 1.3f);

        // Wide, hot trail
        HitEffectSpawner.AddTrailRenderer(gameObject, colorA, colorB, trailTime: 0.28f, startWidth: 0.40f);

        // Flickering orange light — see Update()
        AddProjectileLight(new Color(1f, 0.45f, 0.05f), intensity: 1.8f, outerRadius: 2.2f);
    }

    private void SetupEmberParticles(Color colorA, Color colorB)
    {
        var child = new GameObject("FX_Embers");
        child.transform.SetParent(transform, false);

        var ps  = child.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr = child.GetComponent<ParticleSystemRenderer>();
        psr.material          = HitEffectSpawner.GetAdditiveParticleMaterial();
        psr.sortingLayerID    = SortingLayer.NameToID("Entities");
        psr.sortingOrder      = 98;

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.30f, 0.70f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.30f, 1.20f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor      = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.40f; // float upward

        var emberEmission = ps.emission;
        emberEmission.rateOverTime = 22f;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.10f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(colorB, 0f),
                    new GradientColorKey(colorA, 0.4f),
                    new GradientColorKey(new Color(0.2f, 0.04f, 0f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.6f, 0.5f),
                    new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
    }

    // ── ICE — jagged crystal particles, frost mist ───────────────────────────

    private void SetupIceVisuals(Color colorA, Color colorB)
    {
        // Core crisp ice particles
        SetupCoreParticles(colorA, colorB,
            rateOverTime: 26f,
            lifetime:     new ParticleSystem.MinMaxCurve(0.08f, 0.18f),
            speed:        new ParticleSystem.MinMaxCurve(1.0f,  3.5f),
            size:         new ParticleSystem.MinMaxCurve(0.08f, 0.20f));

        // Stretched crystal shards — radial ejection, look like jagged splinters
        SetupIceCrystalParticles(colorA);

        // Soft frost mist behind the shard
        SetupFrostMistParticles(colorA);

        // Tight cold glow + wider icy aura
        HitEffectSpawner.AddGlowSprite(transform, colorA,                          0.22f, 102);
        HitEffectSpawner.AddGlowSprite(transform, new Color(0.7f, 0.92f, 1f, 1f), 0.52f,  97);

        // Slow pulsing ring
        SetupPulseRing(new Color(0.82f, 0.96f, 1f, 1f), pulseCycleDuration: 0.55f, maxSize: 1.0f);

        // Thin crystalline trail
        HitEffectSpawner.AddTrailRenderer(gameObject, colorA, new Color(1f, 1f, 1f, 1f),
                                          trailTime: 0.22f, startWidth: 0.16f);

        // Steady cool blue light
        AddProjectileLight(new Color(0.3f, 0.7f, 1f), intensity: 1.4f, outerRadius: 1.8f);
    }

    private void SetupIceCrystalParticles(Color colorA)
    {
        var child = new GameObject("FX_IceShards");
        child.transform.SetParent(transform, false);

        var ps  = child.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr = child.GetComponent<ParticleSystemRenderer>();
        psr.material          = HitEffectSpawner.GetAdditiveParticleMaterial();
        psr.renderMode        = ParticleSystemRenderMode.Stretch;
        psr.velocityScale     = 0.9f;
        psr.lengthScale       = 1.8f;
        psr.sortingLayerID    = SortingLayer.NameToID("Entities");
        psr.sortingOrder      = 101;

        var white = new Color(0.90f, 0.97f, 1f, 1f);

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(2.0f,  5.5f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
        main.startColor      = new ParticleSystem.MinMaxGradient(white, colorA);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        var shardsEmission = ps.emission;
        shardsEmission.rateOverTime = 18f;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.05f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(white, 0f),   new GradientColorKey(colorA, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),      new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
    }

    private void SetupFrostMistParticles(Color colorA)
    {
        var child = new GameObject("FX_FrostMist");
        child.transform.SetParent(transform, false);

        var ps  = child.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr = child.GetComponent<ParticleSystemRenderer>();
        psr.material          = HitEffectSpawner.GetAdditiveParticleMaterial();
        psr.sortingLayerID    = SortingLayer.NameToID("Entities");
        psr.sortingOrder      = 97;

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.22f, 0.48f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.10f, 0.55f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.18f, 0.40f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(colorA.r, colorA.g, colorA.b, 0.35f),
                                   new Color(1f, 1f, 1f, 0.18f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        var mistEmission = ps.emission;
        mistEmission.rateOverTime = 7f;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.15f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.82f, 0.96f, 1f), 0f),
                    new GradientColorKey(new Color(0.50f, 0.82f, 1f), 1f) },
            new[] { new GradientAlphaKey(0.4f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
    }

    // ── ALIEN — organic tendrils, purple-teal energy ─────────────────────────

    private void SetupAlienVisuals(Color colorA, Color colorB)
    {
        // Core energy particles
        SetupCoreParticles(colorA, colorB,
            rateOverTime: 85f,
            lifetime:     new ParticleSystem.MinMaxCurve(0.12f, 0.22f),
            speed:        new ParticleSystem.MinMaxCurve(0.3f,  1.8f),
            size:         new ParticleSystem.MinMaxCurve(0.15f, 0.32f));

        // Stretched tentacle tendrils radiating from the core
        SetupTentacleParticles(colorA, colorB);

        // Double glow: teal core + purple halo
        HitEffectSpawner.AddGlowSprite(transform, colorA, 0.30f, 102);
        HitEffectSpawner.AddGlowSprite(transform, colorB, 0.58f,  97);

        // Slow organic pulse ring
        SetupPulseRing(colorB, pulseCycleDuration: 0.50f, maxSize: 1.15f);

        // Wide organic trail
        HitEffectSpawner.AddTrailRenderer(gameObject, colorA, colorB, trailTime: 0.22f, startWidth: 0.36f);

        // Pulsing purple light — see Update()
        AddProjectileLight(new Color(0.55f, 0.1f, 0.9f), intensity: 1.3f, outerRadius: 2.0f);
    }

    private void SetupTentacleParticles(Color colorA, Color colorB)
    {
        var child = new GameObject("FX_Tendrils");
        child.transform.SetParent(transform, false);

        var ps  = child.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr = child.GetComponent<ParticleSystemRenderer>();
        psr.material          = HitEffectSpawner.GetAdditiveParticleMaterial();
        psr.renderMode        = ParticleSystemRenderMode.Stretch;
        psr.velocityScale     = 1.2f;
        psr.lengthScale       = 2.2f;
        psr.sortingLayerID    = SortingLayer.NameToID("Entities");
        psr.sortingOrder      = 101;

        var purple = new Color(0.70f, 0.10f, 1.00f, 1f);

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.5f,  2.2f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startColor      = new ParticleSystem.MinMaxGradient(purple, colorA);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        var tendrilEmission = ps.emission;
        tendrilEmission.rateOverTime = 35f;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.08f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(colorA,  0f),
                    new GradientColorKey(purple,  0.5f),
                    new GradientColorKey(colorB,  1f) },
            new[] { new GradientAlphaKey(1f,  0f),
                    new GradientAlphaKey(0.5f, 0.5f),
                    new GradientAlphaKey(0f,   1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private void SetupCoreParticles(Color colorA, Color colorB,
                                    float rateOverTime,
                                    ParticleSystem.MinMaxCurve lifetime,
                                    ParticleSystem.MinMaxCurve speed,
                                    ParticleSystem.MinMaxCurve size)
    {
        var child = new GameObject("FX");
        child.transform.SetParent(transform, false);

        var ps  = child.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr = child.GetComponent<ParticleSystemRenderer>();
        psr.material          = HitEffectSpawner.GetAdditiveParticleMaterial();
        psr.sortingLayerID    = SortingLayer.NameToID("Entities");
        psr.sortingOrder      = 100;

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = lifetime;
        main.startSpeed      = speed;
        main.startSize       = size;
        main.startColor      = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        var coreEmission = ps.emission;
        coreEmission.rateOverTime = rateOverTime;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.08f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(colorA, 0f), new GradientColorKey(colorB, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),     new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
    }

    private void SetupPulseRing(Color color, float pulseCycleDuration, float maxSize)
    {
        this.pulseCycleDuration = pulseCycleDuration;
        this.pulseMaxSize       = maxSize;

        // Annular ring texture
        const int texSize = 32;
        var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        float r = (texSize - 1) / 2f;
        for (int y = 0; y < texSize; y++)
        for (int x = 0; x < texSize; x++)
        {
            float dist = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
            float a    = Mathf.Clamp01(1f - Mathf.Abs(dist - 0.78f) / 0.22f);
            a          = a * a;
            tex.SetPixel(x, y, new Color(color.r, color.g, color.b, a));
        }
        tex.Apply();

        // Base sprite renders at 0.6 world units; scale is driven in Update()
        float ppu    = texSize / 0.6f;
        var sprite   = Sprite.Create(tex, new Rect(0, 0, texSize, texSize), Vector2.one * 0.5f, ppu);

        Shader sh  = Shader.Find("Sprites/Default")
                  ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat    = new Material(sh);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.renderQueue = 3000;

        var ringGO = new GameObject("FX_PulseRing");
        ringGO.transform.SetParent(transform, false);
        ringGO.transform.localScale = Vector3.one * 0.2f;

        var sr              = ringGO.AddComponent<SpriteRenderer>();
        sr.sprite           = sprite;
        sr.material         = mat;
        sr.sortingLayerName = "Entities";
        sr.sortingOrder     = 96;

        pulseRingTransform = ringGO.transform;
        pulseRingSr        = sr;
    }

    private void AddProjectileLight(Color color, float intensity, float outerRadius)
    {
        try
        {
            var light                   = gameObject.AddComponent<Light2D>();
            light.lightType             = Light2D.LightType.Point;
            light.color                 = color;
            light.intensity             = intensity;
            light.pointLightOuterRadius = outerRadius;
            light.pointLightInnerRadius = outerRadius * 0.30f;
            projLight                   = light;
        }
        catch { /* Light2D unavailable in this build target */ }
    }
}
