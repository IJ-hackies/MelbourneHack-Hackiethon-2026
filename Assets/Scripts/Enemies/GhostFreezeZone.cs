using UnityEngine;
using UnityEngine.Rendering.Universal;

// Icy slow zone left behind when a GhostGooProjectile lands.
// While the player stands inside the radius, their movement is slowed.
// Slowly fades out over its lifetime.
public class GhostFreezeZone : MonoBehaviour
{
    [SerializeField] private float zoneDuration  = 5f;
    [SerializeField] private float dotInterval   = 1.0f;
    [SerializeField] private float checkRadius   = 1.0f;

    private float               elapsed;
    private float               dotTimer;
    private float               attackDamage;
    private bool                playerWasInside;
    private Transform           playerTransform;
    private PlayerStatusEffects playerStatus;
    private Health              playerHealth;
    private PlayerHitEffect     playerHitEffect;
    private SpriteRenderer      sr;
    private Light2D             zoneLight;

    public static GhostFreezeZone Spawn(Vector3 pos, float duration, float radius, float attackDamage)
    {
        var go                = new GameObject("FX_FreezeZone");
        go.transform.position = pos;

        var zone              = go.AddComponent<GhostFreezeZone>();
        zone.zoneDuration     = duration;
        zone.checkRadius      = radius;
        zone.attackDamage     = attackDamage;
        zone.dotTimer         = zone.dotInterval;

        zone.SetupVisual(radius);
        zone.FindPlayer();
        zone.SpawnLandingBurst(pos, radius);

        Destroy(go, duration);
        return zone;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        float lifeT = Mathf.Clamp01(elapsed / zoneDuration);

        // Fade sprite as zone expires
        if (sr != null)
        {
            float alpha = Mathf.Lerp(0.85f, 0f, lifeT);
            var c = sr.color;
            sr.color = new Color(c.r, c.g, c.b, alpha);
        }

        // Fade light with zone
        if (zoneLight != null)
            zoneLight.intensity = Mathf.Lerp(1.2f, 0f, lifeT);

        if (playerTransform == null) return;

        bool inside = Vector2.Distance(transform.position, playerTransform.position) <= checkRadius;

        if (inside && !playerWasInside) playerStatus?.SetSlowed(true);
        if (!inside && playerWasInside)  playerStatus?.SetSlowed(false);
        playerWasInside = inside;

        if (!inside) return;

        dotTimer += Time.deltaTime;
        if (dotTimer >= dotInterval)
        {
            dotTimer = 0f;
            if (playerHealth != null && !playerHealth.IsDead)
            {
                EnemyBase.LogDamageToPlayer(playerHealth, attackDamage * 0.1f, "freeze_zone");
                HitEffectSpawner.SpawnFrostBurst(playerTransform.position);
                playerHitEffect?.PlayHitEffect();
            }
        }
    }

    private void OnDestroy()
    {
        if (playerWasInside) playerStatus?.SetSlowed(false);
    }

    private void FindPlayer()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null) return;
        playerTransform = playerObj.transform;
        playerHealth    = playerObj.GetComponent<Health>();
        playerHitEffect = playerObj.GetComponent<PlayerHitEffect>();

        playerStatus = playerObj.GetComponent<PlayerStatusEffects>();
        if (playerStatus == null)
            playerStatus = playerObj.AddComponent<PlayerStatusEffects>();
    }

    private void SetupVisual(float radius)
    {
        // Runtime circle texture — filled disc with a vivid ring at the edge
        const int texSize = 128;
        var tex   = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        // Brighter, more saturated icy zone colors
        var innerColor = new Color(0.45f, 0.90f, 1.00f, 0.35f); // bright icy blue fill
        var ringColor  = new Color(0.65f, 0.95f, 1.00f, 1.00f); // vivid bright ring

        float r  = (texSize - 1) / 2f;
        float cx = r, cy = r;

        for (int y = 0; y < texSize; y++)
        for (int x = 0; x < texSize; x++)
        {
            float dist   = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float outer  = r;
            float inner  = r * 0.72f;

            if (dist > outer + 1f)
            {
                tex.SetPixel(x, y, Color.clear);
            }
            else if (dist > inner)
            {
                float t     = (dist - inner) / (outer - inner);
                float alpha = Mathf.Clamp01((outer - dist) / 2f);
                tex.SetPixel(x, y, Color.Lerp(innerColor, ringColor, t) * new Color(1, 1, 1, alpha));
            }
            else
            {
                tex.SetPixel(x, y, innerColor);
            }
        }
        tex.Apply();

        float ppu    = texSize / (radius * 2f);
        var sprite   = Sprite.Create(tex, new Rect(0, 0, texSize, texSize),
                                     new Vector2(0.5f, 0.5f), pixelsPerUnit: ppu);

        sr                  = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite           = sprite;
        sr.sortingLayerName = "Background";
        sr.sortingOrder     = 10;
        sr.color            = new Color(1f, 1f, 1f, 0.85f);

        SetupSnowflakes(radius);

        // Dynamic cold light for the zone
        try
        {
            var light                   = gameObject.AddComponent<Light2D>();
            light.lightType             = Light2D.LightType.Point;
            light.color                 = new Color(0.45f, 0.85f, 1f);
            light.intensity             = 1.2f;
            light.pointLightOuterRadius = radius * 1.8f;
            light.pointLightInnerRadius = radius * 0.4f;
            zoneLight                   = light;
        }
        catch { /* Light2D unavailable */ }
    }

    // Burst of ice shards and a bright flash erupting at landing
    private void SpawnLandingBurst(Vector3 pos, float radius)
    {
        // Impact flash
        HitEffectSpawner.SpawnImpactFlash(pos,
            new Color(0.65f, 0.95f, 1f),
            new Color(0.40f, 0.80f, 1f));

        // Radial ice spike burst — fast outward shards
        var go = new GameObject("FX_IceLandBurst");
        go.transform.position = pos;

        var ps  = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr              = go.GetComponent<ParticleSystemRenderer>();
        psr.material         = HitEffectSpawner.GetAdditiveParticleMaterial();
        psr.renderMode       = ParticleSystemRenderMode.Stretch;
        psr.velocityScale    = 0.7f;
        psr.lengthScale      = 2.0f;
        psr.sortingLayerName = "Entities";
        psr.sortingOrder     = 110;

        var white   = new Color(0.92f, 0.98f, 1f, 1f);
        var iceBlue = new Color(0.45f, 0.85f, 1f, 1f);

        var main = ps.main;
        main.duration        = 0.1f;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.20f, 0.45f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(3.0f,  8.0f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor      = new ParticleSystem.MinMaxGradient(white, iceBlue);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.10f;

        var burstEmission = ps.emission;
        burstEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28, 40) });

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.05f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(white,   0f),
                    new GradientColorKey(iceBlue,  0.5f),
                    new GradientColorKey(new Color(0.2f, 0.5f, 0.8f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.5f, 0.6f),
                    new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
        Destroy(go, 1.5f);
    }

    private void SetupSnowflakes(float radius)
    {
        var go = new GameObject("Snowflakes");
        go.transform.SetParent(transform, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        var psr              = go.GetComponent<ParticleSystemRenderer>();
        psr.material         = new Material(urp != null ? urp : fallback);
        psr.sortingLayerName = "Entities";
        psr.sortingOrder     = 5;

        var white      = new Color(0.95f, 0.99f, 1.00f, 1f);
        var lightBlue  = new Color(0.55f, 0.90f, 1.00f, 1f);

        var main = ps.main;
        main.loop            = true;
        main.duration        = zoneDuration;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(2.0f, 3.5f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startColor      = new ParticleSystem.MinMaxGradient(white, lightBlue);
        main.startRotation   = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 80;

        var snowEmission = ps.emission;
        snowEmission.rateOverTime = 12f;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = radius * 0.85f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(white, 0f), new GradientColorKey(lightBlue, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f),
                    new GradientAlphaKey(0.9f, 0.80f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.x       = new ParticleSystem.MinMaxCurve(0f, 0f);
        rot.y       = new ParticleSystem.MinMaxCurve(0f, 0f);
        rot.z       = new ParticleSystem.MinMaxCurve(-45f * Mathf.Deg2Rad, 45f * Mathf.Deg2Rad);

        ps.Play();
    }
}
