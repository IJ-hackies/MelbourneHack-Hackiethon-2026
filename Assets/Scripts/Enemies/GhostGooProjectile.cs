using System.Collections;
using UnityEngine;

// Goo projectile conjured and thrown by GhostAI. Two phases:
//
//   Phase 1 — Conjure: the ball grows from scale 0→1 at the throw position.
//
//   Phase 2 — Flight: travels in an arc to the player's position at the moment of
//             firing — no homing. A visual child rises and falls by arcHeight world
//             units so the arc is purely cosmetic; the ground-level collider position
//             is what matters for landing detection.
//             On landing a GhostFreezeZone is spawned and the projectile is destroyed.
public class GhostGooProjectile : MonoBehaviour
{
    private Color           colorA;
    private Color           colorB;
    private float           damage;
    private Health          playerHealth;
    private PlayerHitEffect playerHitEffect;
    private Transform       playerTransform;

    private Rigidbody2D rb;
    private Transform   visualChild;

    private float   speed;
    private bool    launched;
    private bool    hitDealt;
    private float   flightTime;
    private float   totalFlightTime;
    private Vector2 startPos;
    private Vector2 targetPos;

    [SerializeField] private float arcHeight    = 2.0f;
    [SerializeField] private float peakScale    = 1.4f;
    [SerializeField] private float wobbleAmt    = 0.06f;
    [SerializeField] private float wobbleFreq   = 9f;
    [SerializeField] private float hitRadius    = 0.35f;
    private float zoneDuration;
    private float zoneRadius;

    // ── Factory ───────────────────────────────────────────────────────────────

    public static GhostGooProjectile Conjure(Vector3 pos, float damage,
                                              Color colorA, Color colorB,
                                              Health playerHealth, PlayerHitEffect playerHitEffect,
                                              Transform playerTransform,
                                              float zoneDuration, float zoneRadius,
                                              float conjureDuration = 0.25f)
    {
        var go                    = new GameObject("FX_GhostGoo");
        go.transform.position     = pos;

        var rb                    = go.AddComponent<Rigidbody2D>();
        rb.gravityScale           = 0f;
        rb.isKinematic            = true;

        var proj                  = go.AddComponent<GhostGooProjectile>();
        proj.rb                   = rb;
        proj.damage               = damage;
        proj.colorA               = colorA;
        proj.colorB               = colorB;
        proj.playerHealth         = playerHealth;
        proj.playerHitEffect      = playerHitEffect;
        proj.playerTransform      = playerTransform;
        proj.zoneDuration         = zoneDuration;
        proj.zoneRadius           = zoneRadius;

        proj.SetupVisual(colorA, colorB);
        proj.StartCoroutine(proj.ConjureRoutine(conjureDuration));
        return proj;
    }

    // ── Launch ────────────────────────────────────────────────────────────────

    public void Launch(Transform target, float projectileSpeed)
    {
        if (launched || target == null) return;
        launched          = true;
        speed             = projectileSpeed;
        startPos          = rb.position;
        targetPos         = (Vector2)target.position; // snapshot — no homing

        float dist        = Vector2.Distance(startPos, targetPos);
        totalFlightTime   = Mathf.Max(dist / projectileSpeed, 0.3f);

        Destroy(gameObject, totalFlightTime + 0.5f);
    }

    // ── Conjure grow-in ───────────────────────────────────────────────────────

    private IEnumerator ConjureRoutine(float duration)
    {
        if (visualChild != null) visualChild.localScale = Vector3.zero;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float s = Mathf.SmoothStep(0f, 1f, t);
            if (visualChild != null) visualChild.localScale = Vector3.one * s;
            yield return null;
        }
        if (visualChild != null) visualChild.localScale = Vector3.one;
    }

    // ── Flight ────────────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (!launched || hitDealt) return;

        flightTime += Time.fixedDeltaTime;

        float progress  = Mathf.Clamp01(flightTime / totalFlightTime);
        Vector2 groundPos = Vector2.Lerp(startPos, targetPos, progress);
        rb.MovePosition(groundPos);

        // Direct hit: player is within hitRadius of the ground position.
        if (playerTransform != null &&
            Vector2.Distance(groundPos, (Vector2)playerTransform.position) < hitRadius)
        {
            hitDealt = true;
            DirectHit();
            return;
        }

        if (progress >= 1f)
            Land(groundPos);
    }

    private void Update()
    {
        if (!launched || visualChild == null) return;

        float progress  = Mathf.Clamp01(flightTime / totalFlightTime);
        float arcFactor = Mathf.Sin(progress * Mathf.PI);

        visualChild.localPosition = new Vector3(0f, arcHeight * arcFactor, 0f);

        float baseScale = 1f + (peakScale - 1f) * arcFactor;
        float wobble    = Mathf.Sin(Time.time * wobbleFreq * Mathf.PI * 2f) * wobbleAmt;
        visualChild.localScale = new Vector3(
            baseScale * (1f + wobble),
            baseScale * (1f - wobble),
            1f);
    }

    private void DirectHit()
    {
        if (playerHealth != null && !playerHealth.IsDead)
        {
            playerHealth.TakeDamage(damage);
            HitEffectSpawner.SpawnHit(transform.position, colorA, colorB);
            HitEffectSpawner.SpawnImpactFlash(transform.position, colorA, colorB);
            playerHitEffect?.PlayHitEffect();
        }
        Destroy(gameObject); // no zone on direct hit
    }

    private void Land(Vector2 pos)
    {
        GhostFreezeZone.Spawn(new Vector3(pos.x, pos.y, 0f), zoneDuration, zoneRadius, damage);
        Destroy(gameObject);
    }

    // ── Visuals ───────────────────────────────────────────────────────────────

    private void SetupVisual(Color colorA, Color colorB)
    {
        // All visuals live on a child so the parent's collider stays at ground level.
        var visualGO              = new GameObject("Visual");
        visualGO.transform.SetParent(transform, false);
        visualChild               = visualGO.transform;
        visualChild.localScale    = Vector3.zero; // ConjureRoutine grows it in

        // --- Circle sprite — main ball body ---
        const int texSize = 64;
        var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        float r = (texSize - 1) / 2f;
        for (int y = 0; y < texSize; y++)
        for (int x = 0; x < texSize; x++)
        {
            float dx    = x - r;
            float dy    = y - r;
            float dist  = Mathf.Sqrt(dx * dx + dy * dy);
            // Sharp edge — hard fill inside, 1.5px anti-alias only at the boundary
            float alpha = Mathf.Clamp01((r - 1f - dist) / 1.5f + 1f);
            alpha       = Mathf.Clamp01(alpha);
            // Small specular highlight: bright white offset toward top-left
            float hx    = dx + r * 0.25f;
            float hy    = dy - r * 0.25f;
            float hDist = Mathf.Sqrt(hx * hx + hy * hy);
            float spec  = Mathf.Clamp01(1f - hDist / (r * 0.45f));
            Color c     = Color.Lerp(colorA, Color.white, spec * 0.6f);
            tex.SetPixel(x, y, new Color(c.r, c.g, c.b, alpha));
        }
        tex.Apply();

        // 210 PPU → 64px = ~0.3 world units diameter
        var sprite          = Sprite.Create(tex, new Rect(0, 0, texSize, texSize),
                                            new Vector2(0.5f, 0.5f), pixelsPerUnit: 210f);
        // Glow behind the ball — additive soft circle makes it look lit from within
        HitEffectSpawner.AddGlowSprite(visualChild, colorA, 0.55f, 95);

        var sr              = visualGO.AddComponent<SpriteRenderer>();
        sr.sprite           = sprite;
        sr.sortingLayerName = "Entities";
        sr.sortingOrder     = 100;

        // --- Drip particles — slow, fall downward, suggest viscous goo ---
        var dripGO              = new GameObject("Drips");
        dripGO.transform.SetParent(visualGO.transform, false);

        var ps = dripGO.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr              = dripGO.GetComponent<ParticleSystemRenderer>();
        psr.material         = HitEffectSpawner.GetAdditiveParticleMaterial();
        psr.sortingLayerName = "Entities";
        psr.sortingOrder     = 99; // just behind the ball

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startColor      = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.4f; // drips fall down

        var emission = ps.emission;
        emission.rateOverTime = 12f;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.12f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(colorA, 0f), new GradientColorKey(colorB, 1f) },
            new[] { new GradientAlphaKey(0.85f, 0f),  new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
    }

}
