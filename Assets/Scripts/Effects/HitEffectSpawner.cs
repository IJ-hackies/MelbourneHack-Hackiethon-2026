using UnityEngine;

// Spawns one-shot particle effects at a world position.
// Call the static methods from anywhere — no scene reference needed.
public static class HitEffectSpawner
{
    // URP requires an explicit material — fall back to Sprites/Default if URP shader missing
    private static Material GetParticleMaterial()
    {
        Shader urp = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        return new Material(urp != null ? urp : fallback);
    }

    // Additive-blended particle material — overlapping particles add together, simulating glow.
    public static Material GetAdditiveParticleMaterial()
    {
        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        var mat = new Material(urp != null ? urp : fallback);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        return mat;
    }

    // Short bright burst — call this when a projectile makes contact.
    // Starts white (flash) then fades through the projectile's colours.
    public static void SpawnImpactFlash(Vector3 position, Color colorA, Color colorB)
    {
        var go = new GameObject("FX_ImpactFlash");
        go.transform.position = position;

        var ps  = go.AddComponent<ParticleSystem>();
        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.material         = GetAdditiveParticleMaterial();
        psr.sortingLayerName = "Entities";
        psr.sortingOrder     = 200;

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration        = 0.1f;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(5f, 12f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.08f, 0.25f);
        main.startColor      = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 14) });

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.05f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(colorA, 0.35f), new GradientColorKey(colorB, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

        ps.Play();
        Object.Destroy(go, 0.5f);
    }

    // Adds a soft radial glow sprite as a child of `parent` — simulates bloom without post-processing.
    // Uses additive blending so it brightens whatever is underneath.
    public static void AddGlowSprite(Transform parent, Color color, float size = 0.6f, int sortOrder = 98)
    {
        const int texSize = 32;
        var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        float r = (texSize - 1) / 2f;
        for (int y = 0; y < texSize; y++)
        for (int x = 0; x < texSize; x++)
        {
            float dist  = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
            float alpha = Mathf.Clamp01(1f - dist);
            alpha       = alpha * alpha; // quadratic falloff — bright centre, soft edge
            tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
        }
        tex.Apply();

        var glowGO = new GameObject("FX_GlowCore");
        glowGO.transform.SetParent(parent, false);
        glowGO.transform.localPosition = Vector3.zero;
        glowGO.transform.localScale    = Vector3.one;

        float ppu    = texSize / size; // so the sprite renders at `size` world units wide
        var sprite   = Sprite.Create(tex, new Rect(0, 0, texSize, texSize), Vector2.one * 0.5f, ppu);

        Shader sh = Shader.Find("Sprites/Default")
                 ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat   = new Material(sh);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.renderQueue = 3000;

        var sr              = glowGO.AddComponent<SpriteRenderer>();
        sr.sprite           = sprite;
        sr.material         = mat;
        sr.sortingLayerName = "Entities";
        sr.sortingOrder     = sortOrder;
    }

    // Adds a TrailRenderer to `go` — produces a motion-streak behind fast-moving projectiles.
    public static void AddTrailRenderer(GameObject go, Color colorA, Color colorB,
                                        float trailTime = 0.12f, float startWidth = 0.2f)
    {
        var tr = go.AddComponent<TrailRenderer>();
        tr.time              = trailTime;
        tr.startWidth        = startWidth;
        tr.endWidth          = 0f;
        tr.minVertexDistance = 0.05f;
        tr.autodestruct      = false;

        Shader sh = Shader.Find("Sprites/Default")
                 ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat = new Material(sh);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        tr.material = mat;

        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(colorA, 0.4f), new GradientColorKey(colorB, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0f, 1f) }
        );
        tr.colorGradient = g;
        tr.sortingLayerName = "Entities";
        tr.sortingOrder     = 101;
    }

    // Generic hit burst — pass the two colours for the effect
    public static void SpawnHit(Vector3 position, Color colorA, Color colorB)
    {
        GameObject go = new GameObject("FX_Hit");
        go.transform.position = position;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        go.GetComponent<ParticleSystemRenderer>().material = GetParticleMaterial();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.4f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.gravityModifier = -0.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled = true;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 20, 30) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(colorA, 0f), new GradientColorKey(colorB, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

        ps.Play();
        Object.Destroy(go, 1f);
    }

    // Fireball projectile — cosmetic only. A GameObject moves from `from` to `to`
    // and the particle system rides it, leaving a fire trail in world space.
    // Damage is dealt by the calling AI independently.
    public static void SpawnFireball(Vector3 from, Vector3 to, Color colorA, Color colorB)
    {
        float distance    = Vector3.Distance(from, to);
        float speed       = 7f;
        float travelTime  = Mathf.Max(distance / speed, 0.05f);

        var go = new GameObject("FX_Fireball");
        go.transform.position = from;
        go.AddComponent<FireballMover>().Init(to, speed);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        go.GetComponent<ParticleSystemRenderer>().material = GetParticleMaterial();

        // Particles emitted in world space so they stay behind as the GO moves — natural trail
        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = 0.15f;
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0f, 1f); // slight jitter only
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

        ps.Play();
        Object.Destroy(go, travelTime + 0.3f);
    }

    // Ground crack — spawned at the DragonNewt's feet on landing.
    // Shoots a small burst of flat, earthy particles radially outward to read as a crack.
    public static void SpawnGroundCrack(Vector3 position, Color colorA, Color colorB)
    {
        GameObject go = new GameObject("FX_GroundCrack");
        go.transform.position = position;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        go.GetComponent<ParticleSystemRenderer>().material = GetParticleMaterial();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration        = 0.1f;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(1.0f, 1.5f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startColor      = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled = true;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16, 24) });

        // Flat disc shape so particles radiate outward along the ground plane
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.05f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(colorA, 0f), new GradientColorKey(colorB, 0.5f) },
            new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0.3f, 0.5f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f));

        ps.Play();
        Object.Destroy(go, 2f);
    }

    // Small icy burst — plays on the player during freeze zone DoT ticks
    public static void SpawnFrostBurst(Vector3 position)
    {
        var lightBlue = new Color(0.55f, 0.85f, 1.00f, 1f);
        var white     = new Color(0.90f, 0.97f, 1.00f, 1f);

        GameObject go = new GameObject("FX_FrostBurst");
        go.transform.position = position;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        go.GetComponent<ParticleSystemRenderer>().material = GetParticleMaterial();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration        = 0.2f;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.8f, 2.5f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startColor      = new ParticleSystem.MinMaxGradient(lightBlue, white);
        main.gravityModifier = -0.2f; // drift slightly upward — icy crystals
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled = true;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 10, 16) });

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.15f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(white, 0f), new GradientColorKey(lightBlue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),    new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
        Object.Destroy(go, 1f);
    }

    // Subtle blood — plays on the player when hit
    public static void SpawnBlood(Vector3 position)
    {
        GameObject go = new GameObject("FX_Blood");
        go.transform.position = position;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        go.GetComponent<ParticleSystemRenderer>().material = GetParticleMaterial();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.6f, 0f, 0f, 1f),
            new Color(0.9f, 0.05f, 0.05f, 1f)
        );
        main.gravityModifier = 1.5f; // fall downward
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled = true;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 14) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.8f, 0f, 0f), 0f), new GradientColorKey(new Color(0.3f, 0f, 0f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
        Object.Destroy(go, 1f);
    }
}

// Moves a fireball particle GameObject toward a target at fixed speed.
// Internal helper — only used by HitEffectSpawner.SpawnFireball.
public class FireballMover : MonoBehaviour
{
    private Vector3 target;
    private float   speed;

    public void Init(Vector3 target, float speed)
    {
        this.target = target;
        this.speed  = speed;
    }

    private void Update()
    {
        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
    }
}
