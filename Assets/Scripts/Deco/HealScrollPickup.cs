using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to the HealScroll prefab. Handles hover animation, idle particles,
/// heal-on-contact logic, and the shrink+fade pickup animation.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class HealScrollPickup : MonoBehaviour
{
    // ── Hover ────────────────────────────────────────────────────────────────
    private const float HoverAmplitude = 0.22f;
    private const float HoverSpeed     = 2.2f;
    private const float PulseAmount    = 0.12f;
    private const float PulseSpeed     = 2.8f;

    // ── Idle particles ───────────────────────────────────────────────────────
    private const float ParticleInterval = 0.28f;
    private const float ParticleLifetime = 0.9f;
    private const float ParticleDriftY   = 0.9f;   // world units per second upward
    private const float ParticleSize     = 0.07f;
    private const float ParticleSpread   = 0.18f;   // horizontal random offset radius
    private static readonly Color ParticleColor = new Color(0.2f, 1f, 0.35f, 0.85f);

    // ── Heal ─────────────────────────────────────────────────────────────────
    private const float HealPercent = 0.05f;        // 5% of max HP

    // ── Pickup animation ─────────────────────────────────────────────────────
    private const float DisappearDuration = 0.28f;

    // ── State ────────────────────────────────────────────────────────────────
    private SpriteRenderer      sr;
    private Vector3             basePos;
    private Vector3             baseScale;
    private bool                collected;
    private float               hoverOffset;
    private List<GameObject>    activeParticles = new List<GameObject>();

    private static Sprite  pixelSprite;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();

        var col = GetComponent<Collider2D>();
        col.isTrigger = true;

        if (GetComponent<Rigidbody2D>() == null)
        {
            var rb        = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType   = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
        }
    }

    private void Start()
    {
        basePos    = transform.position;
        baseScale  = transform.localScale;
        hoverOffset = Random.Range(0f, Mathf.PI * 2f);

        if (pixelSprite == null)
            pixelSprite = MakePixelSprite();

        StartCoroutine(EmitParticles());
    }

    private void Update()
    {
        if (collected) return;

        float t = Time.time + hoverOffset;

        // Hover — bob up and down
        transform.position = basePos + Vector3.up * (Mathf.Sin(t * HoverSpeed) * HoverAmplitude);

        // Scale pulse — gentle grow/shrink
        float scale = 1f + Mathf.Sin(t * PulseSpeed) * PulseAmount;
        transform.localScale = baseScale * scale;
    }

    // ── Pickup trigger ───────────────────────────────────────────────────────

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected) return;

        // The player may collide via a child hitbox — walk up to find Health/tag on the root.
        var health = other.GetComponentInParent<Health>();
        if (health == null) return;
        if (!other.transform.root.CompareTag("Player")) return;

        // Can't pick up if already full
        float gap = health.Max - health.Current;
        if (gap <= 0f) return;

        // Heal up to 5% of max HP, capped at the remaining gap (won't overheal)
        float amount = Mathf.Min(health.Max * HealPercent, gap);
        health.Heal(amount);

        collected = true;

        // Visual feedback on player — PlayerHitEffect lives on the sprite child, so search from root
        var hitEffect = other.transform.root.GetComponentInChildren<PlayerHitEffect>();
        hitEffect?.PlayHealEffect();

        // SFX
        SFXManager.Instance?.PlayHealScroll();

        StartCoroutine(DisappearAnimation());
    }

    // ── Animations ───────────────────────────────────────────────────────────

    private IEnumerator DisappearAnimation()
    {
        // Immediately destroy all in-flight particles
        foreach (var p in activeParticles)
            if (p != null) Destroy(p);
        activeParticles.Clear();

        // collected = true already stops EmitParticles on its next iteration — no StopAllCoroutines needed
        float elapsed    = 0f;
        Color startColor = sr.color;

        while (elapsed < DisappearDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / DisappearDuration;

            transform.localScale = baseScale * (1f - t);

            Color c = startColor;
            c.a = Mathf.Lerp(1f, 0f, t);
            sr.color = c;

            yield return null;
        }

        Destroy(gameObject);
    }

    private IEnumerator EmitParticles()
    {
        while (!collected)
        {
            SpawnParticle();
            yield return new WaitForSeconds(ParticleInterval);
        }
    }

    private void SpawnParticle()
    {
        Vector3 spawnPos = transform.position
                         + new Vector3(Random.Range(-ParticleSpread, ParticleSpread), 0f, 0f);

        GameObject p  = new GameObject("HealParticle");
        p.transform.position   = spawnPos;
        p.transform.localScale = Vector3.one * ParticleSize;

        SpriteRenderer psr  = p.AddComponent<SpriteRenderer>();
        psr.sprite          = pixelSprite;
        psr.sortingLayerID  = sr.sortingLayerID;
        psr.sortingOrder    = sr.sortingOrder + 1;
        psr.color           = ParticleColor;

        activeParticles.Add(p);
        StartCoroutine(DriftAndFade(psr, p, activeParticles));
    }

    private static IEnumerator DriftAndFade(SpriteRenderer target, GameObject obj, List<GameObject> tracker)
    {
        float elapsed = 0f;
        Color c       = target.color;

        while (elapsed < ParticleLifetime)
        {
            if (obj == null) yield break; // destroyed early by DisappearAnimation
            elapsed += Time.deltaTime;
            float t = elapsed / ParticleLifetime;

            obj.transform.position += Vector3.up * ParticleDriftY * Time.deltaTime;

            c.a = t < 0.4f ? ParticleColor.a : Mathf.Lerp(ParticleColor.a, 0f, (t - 0.4f) / 0.6f);
            target.color = c;

            yield return null;
        }

        tracker.Remove(obj);
        Destroy(obj);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Sprite MakePixelSprite()
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }
}
