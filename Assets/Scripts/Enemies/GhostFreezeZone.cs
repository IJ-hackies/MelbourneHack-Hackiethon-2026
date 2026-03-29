using UnityEngine;

// Icy slow zone left behind when a GhostGooProjectile lands.
// While the player stands inside the radius, their movement is slowed.
// Slowly fades out over its lifetime.
public class GhostFreezeZone : MonoBehaviour
{
    [SerializeField] private float zoneDuration  = 5f;
    [SerializeField] private float dotInterval   = 1.0f;  // seconds between damage ticks
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

    public static GhostFreezeZone Spawn(Vector3 pos, float duration, float radius, float attackDamage)
    {
        var go                = new GameObject("FX_FreezeZone");
        go.transform.position = pos;

        var zone              = go.AddComponent<GhostFreezeZone>();
        zone.zoneDuration     = duration;
        zone.checkRadius      = radius;
        zone.attackDamage     = attackDamage;
        zone.dotTimer         = zone.dotInterval; // ready to tick immediately on first contact

        zone.SetupVisual(radius);
        zone.FindPlayer();

        Destroy(go, duration);
        return zone;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        // Fade sprite as zone expires
        if (sr != null)
        {
            float alpha = Mathf.Lerp(0.75f, 0f, elapsed / zoneDuration);
            var c = sr.color;
            sr.color = new Color(c.r, c.g, c.b, alpha);
        }

        if (playerTransform == null) return;

        bool inside = Vector2.Distance(transform.position, playerTransform.position) <= checkRadius;

        // Toggle slow on enter / exit
        if (inside && !playerWasInside) playerStatus?.SetSlowed(true);
        if (!inside && playerWasInside)  playerStatus?.SetSlowed(false);
        playerWasInside = inside;

        if (!inside) return;

        // DoT timer only accumulates while the player is standing in the zone
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
        // Ensure slow is cleared if the zone expires while the player is still inside
        if (playerWasInside) playerStatus?.SetSlowed(false);
    }

    private void FindPlayer()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null) return;
        playerTransform = playerObj.transform;
        playerHealth    = playerObj.GetComponent<Health>();
        playerHitEffect = playerObj.GetComponent<PlayerHitEffect>();

        // Auto-add PlayerStatusEffects if missing so it works without manual Inspector setup
        playerStatus = playerObj.GetComponent<PlayerStatusEffects>();
        if (playerStatus == null)
            playerStatus = playerObj.AddComponent<PlayerStatusEffects>();
    }

    private void SetupVisual(float radius)
    {
        // Runtime circle texture — filled disc with a brighter ring at the edge
        const int texSize = 128;
        var tex   = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        var innerColor = new Color(0.55f, 0.85f, 1.00f, 0.30f); // pale icy blue fill
        var ringColor  = new Color(0.30f, 0.65f, 1.00f, 0.80f); // brighter border ring

        float r  = (texSize - 1) / 2f;
        float cx = r, cy = r;

        for (int y = 0; y < texSize; y++)
        for (int x = 0; x < texSize; x++)
        {
            float dist   = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float outer  = r;
            float inner  = r * 0.78f;  // ring starts here

            if (dist > outer + 1f)
            {
                tex.SetPixel(x, y, Color.clear);
            }
            else if (dist > inner)
            {
                // Ring — blend innerColor → ringColor, fade out at very edge
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

        // PPU: texSize / (radius * 2) world units
        float ppu    = texSize / (radius * 2f);
        var sprite   = Sprite.Create(tex, new Rect(0, 0, texSize, texSize),
                                     new Vector2(0.5f, 0.5f), pixelsPerUnit: ppu);

        sr                  = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite           = sprite;
        sr.sortingLayerName = "Background";
        sr.sortingOrder     = 10;
        sr.color            = new Color(1f, 1f, 1f, 0.75f);

        SetupSnowflakes(radius);
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

        var white      = new Color(0.92f, 0.97f, 1.00f, 1f);
        var lightBlue  = new Color(0.55f, 0.85f, 1.00f, 1f);

        var main = ps.main;
        main.loop            = true;
        main.duration        = zoneDuration;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(2.0f, 3.5f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.05f, 0.20f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        main.startColor      = new ParticleSystem.MinMaxGradient(white, lightBlue);
        main.startRotation   = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.gravityModifier = -0.04f; // drift very gently upward
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 60;

        var emission = ps.emission;
        emission.rateOverTime = 8f;

        // Emit from within the zone disc so flakes appear throughout the area
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = radius * 0.85f;

        // Fade in and out over lifetime
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(white, 0f), new GradientColorKey(lightBlue, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.15f),
                    new GradientAlphaKey(0.8f, 0.80f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        // Slow rotation over lifetime for a tumbling snowflake look
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z       = new ParticleSystem.MinMaxCurve(-45f * Mathf.Deg2Rad, 45f * Mathf.Deg2Rad);

        ps.Play();
    }
}
