using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Manages status effects applied to the player by enemies.
// Auto-added at runtime if missing — no manual Inspector setup required.
//
// Slow   — two independent sources (zone toggle + timed hit), strongest wins.
// Burn   — refreshing DoT; reapplying resets the timer, never stacks.
// Poison — refreshing vulnerability; reapplying resets the timer, never stacks.
// Bleed  — refreshing DoT; reapplying resets the timer, never stacks.
public class PlayerStatusEffects : MonoBehaviour
{
    [Header("Zone Slow")]
    [SerializeField] private float zoneSlowMultiplier = 0.4f;

    private PlayerMovement  movement;
    private Health          health;
    private PlayerHitEffect playerHitEffect;

    // ── Public state (read by PlayerHUD) ─────────────────────────────────────

    public bool  IsZoneSlowed       => isZoneSlowed;
    public bool  IsTimedSlowed      => timedSlowMult < 1f;
    public float TimedSlowRemaining { get; private set; }

    public bool  IsBurning          => isBurning;
    public float BurnRemaining      { get; private set; }

    public bool  IsPoisoned         => isPoisoned;
    public float PoisonRemaining    { get; private set; }

    public bool  IsBleeding         => isBleeding;
    public float BleedRemaining     { get; private set; }

    // ── Slow ──────────────────────────────────────────────────────────────────

    private bool      isZoneSlowed;
    private float     timedSlowMult    = 1f;
    private Coroutine timedSlowRoutine;

    public void SetSlowed(bool slowed)
    {
        if (isZoneSlowed == slowed) return;
        isZoneSlowed = slowed;
        RefreshSpeed();
    }

    public void ApplyTimedSlow(float multiplier, float duration)
    {
        if (timedSlowRoutine != null) StopCoroutine(timedSlowRoutine);
        timedSlowRoutine = StartCoroutine(TimedSlowRoutine(multiplier, duration));
    }

    private void RefreshSpeed()
    {
        float zoneMult  = isZoneSlowed ? zoneSlowMultiplier : 1f;
        float effective = Mathf.Min(zoneMult, timedSlowMult);
        movement.SpeedMultiplier = effective;
        SetArrowsVisible(effective < 1f);
    }

    private IEnumerator TimedSlowRoutine(float multiplier, float duration)
    {
        timedSlowMult      = multiplier;
        TimedSlowRemaining = duration;
        RefreshSpeed();
        while (TimedSlowRemaining > 0f)
        {
            yield return null;
            TimedSlowRemaining -= Time.deltaTime;
        }
        TimedSlowRemaining = 0f;
        timedSlowMult      = 1f;
        timedSlowRoutine   = null;
        RefreshSpeed();
    }

    // ── Burn ──────────────────────────────────────────────────────────────────

    private bool           isBurning;
    private Coroutine      burnRoutine;
    private ParticleSystem burnParticles;

    // damagePerTick = AttackDamage * 0.1 (computed by the caller)
    public void ApplyBurn(float damagePerTick, float duration)
    {
        if (burnRoutine != null) StopCoroutine(burnRoutine);
        isBurning   = true;
        burnRoutine = StartCoroutine(BurnRoutine(damagePerTick, duration));
        UpdateBurnVisual();
    }

    private IEnumerator BurnRoutine(float damagePerTick, float duration)
    {
        BurnRemaining = duration;
        float tickAccum = 0f;
        while (BurnRemaining > 0f)
        {
            yield return null;
            float dt   = Time.deltaTime;
            BurnRemaining -= dt;
            tickAccum     += dt;
            if (tickAccum >= 1f)
            {
                tickAccum -= 1f;
                if (health != null && !health.IsDead)
                {
                    health.TakeDamage(damagePerTick);
                    HitEffectSpawner.SpawnHit(transform.position,
                        new Color(1f, 0.45f, 0.05f), new Color(1f, 0.85f, 0.1f));
                    playerHitEffect?.PlayHitEffect();
                }
            }
        }
        BurnRemaining = 0f;
        isBurning     = false;
        burnRoutine   = null;
        UpdateBurnVisual();
    }

    private void UpdateBurnVisual()
    {
        if (isBurning)
        {
            if (burnParticles == null) CreateBurnParticles();
            var em = burnParticles.emission;
            em.rateOverTime = 14f;
            if (!burnParticles.isPlaying) burnParticles.Play();
        }
        else if (burnParticles != null && burnParticles.isPlaying)
        {
            burnParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void CreateBurnParticles()
    {
        var go = new GameObject("BurnParticles");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.15f, 0f);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        var psr              = go.GetComponent<ParticleSystemRenderer>();
        psr.material         = new Material(urp != null ? urp : fallback);
        psr.sortingLayerName = "Entities";
        psr.sortingOrder     = 201;

        var orange = new Color(1.0f, 0.45f, 0.05f, 1f);
        var yellow = new Color(1.0f, 0.90f, 0.15f, 1f);

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
        main.startColor      = new ParticleSystem.MinMaxGradient(orange, yellow);
        main.gravityModifier = -0.6f; // flames drift upward
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 14f; // updated per stack in UpdateBurnVisual

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.18f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(yellow, 0f), new GradientColorKey(orange, 0.6f),
                    new GradientColorKey(new Color(0.3f, 0.05f, 0f), 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.7f),
                    new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        burnParticles = ps;
    }

    // ── Poison ────────────────────────────────────────────────────────────────

    private bool           isPoisoned;
    private Coroutine      poisonRoutine;
    private ParticleSystem poisonParticles;

    public void ApplyPoison(float duration)
    {
        if (poisonRoutine != null) StopCoroutine(poisonRoutine);
        isPoisoned    = true;
        poisonRoutine = StartCoroutine(PoisonRoutine(duration));
        UpdatePoisonVisual();
    }

    private IEnumerator PoisonRoutine(float duration)
    {
        PoisonRemaining = duration;
        while (PoisonRemaining > 0f)
        {
            yield return null;
            PoisonRemaining -= Time.deltaTime;
        }
        PoisonRemaining = 0f;
        isPoisoned      = false;
        poisonRoutine   = null;
        UpdatePoisonVisual();
    }

    private void UpdatePoisonVisual()
    {
        if (health != null)
            health.DamageMultiplier = isPoisoned ? 1.1f : 1f;

        if (isPoisoned)
        {
            if (poisonParticles == null) CreatePoisonParticles();
            var em = poisonParticles.emission;
            em.rateOverTime = 10f;
            if (!poisonParticles.isPlaying) poisonParticles.Play();
        }
        else if (poisonParticles != null && poisonParticles.isPlaying)
        {
            poisonParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void CreatePoisonParticles()
    {
        var go = new GameObject("PoisonParticles");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.05f, 0f);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        var psr              = go.GetComponent<ParticleSystemRenderer>();
        psr.material         = new Material(urp != null ? urp : fallback);
        psr.sortingLayerName = "Entities";
        psr.sortingOrder     = 201;

        var purple     = new Color(0.55f, 0.05f, 0.70f, 1f);
        var toxicGreen = new Color(0.25f, 0.80f, 0.20f, 1f);

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor      = new ParticleSystem.MinMaxGradient(purple, toxicGreen);
        main.gravityModifier = -0.4f; // bubbles drift upward
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 10f; // updated per stack in UpdatePoisonVisual

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.20f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(toxicGreen, 0f), new GradientColorKey(purple, 0.5f),
                    new GradientColorKey(new Color(0.08f, 0f, 0.12f), 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.6f),
                    new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        poisonParticles = ps;
    }

    // ── Bleed ─────────────────────────────────────────────────────────────────

    private bool           isBleeding;
    private Coroutine      bleedRoutine;
    private ParticleSystem bleedParticles;

    // damagePerTick = AttackDamage * 0.1 (computed by the caller)
    public void ApplyBleed(float damagePerTick, float duration)
    {
        if (bleedRoutine != null) StopCoroutine(bleedRoutine);
        isBleeding   = true;
        bleedRoutine = StartCoroutine(BleedRoutine(damagePerTick, duration));
        UpdateBleedVisual();
    }

    private IEnumerator BleedRoutine(float damagePerTick, float duration)
    {
        BleedRemaining = duration;
        float tickAccum = 0f;
        while (BleedRemaining > 0f)
        {
            yield return null;
            float dt    = Time.deltaTime;
            BleedRemaining -= dt;
            tickAccum      += dt;
            if (tickAccum >= 1f)
            {
                tickAccum -= 1f;
                if (health != null && !health.IsDead)
                {
                    health.TakeDamage(damagePerTick);
                    HitEffectSpawner.SpawnHit(transform.position,
                        new Color(0.75f, 0.02f, 0.02f), new Color(0.40f, 0.00f, 0.00f));
                    playerHitEffect?.PlayHitEffect();
                }
            }
        }
        BleedRemaining = 0f;
        isBleeding     = false;
        bleedRoutine   = null;
        UpdateBleedVisual();
    }

    private void UpdateBleedVisual()
    {
        if (isBleeding)
        {
            if (bleedParticles == null) CreateBleedParticles();
            var em = bleedParticles.emission;
            em.rateOverTime = 8f;
            if (!bleedParticles.isPlaying) bleedParticles.Play();
        }
        else if (bleedParticles != null && bleedParticles.isPlaying)
        {
            bleedParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void CreateBleedParticles()
    {
        var go = new GameObject("BleedParticles");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.2f, 0f);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        var psr              = go.GetComponent<ParticleSystemRenderer>();
        psr.material         = new Material(urp != null ? urp : fallback);
        psr.sortingLayerName = "Entities";
        psr.sortingOrder     = 201;

        var crimson  = new Color(0.80f, 0.03f, 0.03f, 1f);
        var darkRed  = new Color(0.35f, 0.00f, 0.00f, 1f);

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.35f, 0.60f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor      = new ParticleSystem.MinMaxGradient(crimson, darkRed);
        main.gravityModifier = 1.8f;  // droplets fall downward
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 8f;   // updated per stack in UpdateBleedVisual

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.20f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(crimson, 0f),   new GradientColorKey(darkRed, 0.5f),
                    new GradientColorKey(new Color(0.15f, 0f, 0f), 1f) },
            new[] { new GradientAlphaKey(1.0f, 0f),      new GradientAlphaKey(0.7f, 0.5f),
                    new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        bleedParticles = ps;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        movement        = GetComponent<PlayerMovement>();
        health          = GetComponent<Health>();
        playerHitEffect = GetComponent<PlayerHitEffect>();
    }

    private void Start()
    {
        CreateArrowIndicators();
        SetArrowsVisible(false);
    }

    // ── Slow arrow visual ─────────────────────────────────────────────────────

    private readonly List<GameObject> arrowObjects = new List<GameObject>();

    private void CreateArrowIndicators()
    {
        var lightBlue = new Color(0.45f, 0.80f, 1.00f, 0.65f);
        var darkBlue  = new Color(0.10f, 0.30f, 0.90f, 0.65f);

        (Color col, float x, float y)[] layout =
        {
            (lightBlue, -0.09f,  0.17f),
            (darkBlue,   0.09f,  0.17f),
            (lightBlue,  0.00f,  0.10f),
            (darkBlue,  -0.09f,  0.03f),
            (lightBlue,  0.09f,  0.03f),
        };

        foreach (var (col, x, y) in layout)
            arrowObjects.Add(CreateArrow(col, new Vector3(x, y, 0f)));
    }

    private GameObject CreateArrow(Color color, Vector3 localPos)
    {
        var go = new GameObject("SlowArrow");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;

        var tex    = MakeArrowTexture(6, 8, color);
        var sprite = Sprite.Create(tex, new Rect(0, 0, 6, 8),
                                   new Vector2(0.5f, 0.5f), pixelsPerUnit: 180f);

        var sr              = go.AddComponent<SpriteRenderer>();
        sr.sprite           = sprite;
        sr.color            = color;
        sr.sortingLayerName = "Entities";
        sr.sortingOrder     = 200;

        go.AddComponent<SlowArrowBobber>();
        return go;
    }

    private static Texture2D MakeArrowTexture(int w, int h, Color fill)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;

        for (int py = 0; py < h; py++)
        for (int px = 0; px < w; px++)
            tex.SetPixel(px, py, Color.clear);

        int shaftW   = w / 3;
        int shaftTop = h / 2;
        int cx       = w / 2;

        for (int py = shaftTop; py < h; py++)
        for (int px = cx - shaftW / 2; px <= cx + shaftW / 2; px++)
            if (px >= 0 && px < w)
                tex.SetPixel(px, py, fill);

        for (int py = 0; py < shaftTop; py++)
        {
            float t        = (float)py / (shaftTop - 1);
            float halfFill = (w / 2f - 0.5f) * t;
            for (int px = 0; px < w; px++)
                if (Mathf.Abs(px - cx) <= halfFill)
                    tex.SetPixel(px, py, fill);
        }

        tex.Apply();
        return tex;
    }

    private void SetArrowsVisible(bool visible)
    {
        foreach (var arrow in arrowObjects)
            if (arrow != null) arrow.SetActive(visible);
    }
}
