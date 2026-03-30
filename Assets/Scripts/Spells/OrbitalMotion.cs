using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attached to an orbital projectile spawned by SpellExecutor.
/// Orbits the parent (player) and deals damage to enemies on contact.
/// Applies all effect/status tags the same way ProjectileHandler does.
/// </summary>
public class OrbitalMotion : MonoBehaviour
{
    private SpellData spell;
    private float angle;
    private float lifetime;
    private float spawnTime;
    private const float radius = 1.5f;
    private const float orbitSpeed = 200f; // degrees per second
    private const float damageCooldown = 0.6f;
    private const float DefaultLifetime = 6f;
    private const float MaxScale = 1.5f;

    // Per-enemy cooldown so the orbital doesn't deal damage every frame
    private readonly Dictionary<GameObject, float> hitCooldowns = new();

    private Health playerHealth;

    private void Awake()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            playerHealth = playerObj.GetComponent<Health>();
    }

    public void Init(SpellData spellData, int orbitIndex)
    {
        spell = spellData;
        angle = orbitIndex * 120f; // space up to 3 orbitals 120° apart
        spawnTime = Time.time;
        lifetime = DefaultLifetime;

        // Apply Gemini-controlled visuals
        Color primary = !string.IsNullOrEmpty(spell.projectileColor)
            && ColorUtility.TryParseHtmlString(spell.projectileColor.StartsWith("#") ? spell.projectileColor : "#" + spell.projectileColor, out Color pc)
            ? pc : ProjectileHandler.ElementToColor(spell.element);

        float scale = Mathf.Clamp(spell.projectileScale > 0f ? spell.projectileScale : 1f, 0.3f, MaxScale);
        transform.localScale = Vector3.one * scale;

        float glowSize = Mathf.Clamp(spell.glowSize > 0f ? spell.glowSize : 0.35f, 0.1f, 1f);
        HitEffectSpawner.AddGlowSprite(transform, primary, glowSize, 5);
    }

    private void Update()
    {
        if (spell == null) return;

        float elapsed = Time.time - spawnTime;
        if (elapsed >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        // Fade out during the last second
        float fadeStart = lifetime - 1f;
        if (elapsed > fadeStart)
        {
            float alpha = 1f - (elapsed - fadeStart);
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                Color c = sr.color;
                c.a = alpha;
                sr.color = c;
            }
        }

        angle += orbitSpeed * Time.deltaTime;
        float rad = angle * Mathf.Deg2Rad;
        transform.localPosition = new Vector2(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!other.CompareTag("Enemy") && other.gameObject.layer != LayerMask.NameToLayer("Ghost")) return;

        GameObject enemy = other.gameObject;
        float now = Time.time;

        if (hitCooldowns.TryGetValue(enemy, out float lastHit) && now - lastHit < damageCooldown)
            return;

        var h = other.GetComponent<Health>();
        if (h == null || h.IsDead) return;

        h.TakeDamage(spell.damage);
        hitCooldowns[enemy] = now;
        SessionLogger.Instance?.RecordDamageDealt(spell.element, spell.damage);

        if (spell.HasTag(SpellTag.LIFESTEAL))
            playerHealth?.Heal(spell.damage * 0.3f);

        if (spell.HasTag(SpellTag.PUSH))
        {
            var enemyRb = enemy.GetComponent<Rigidbody2D>();
            Vector2 pushDir = ((Vector2)enemy.transform.position - (Vector2)transform.position).normalized;
            enemyRb?.AddForce(pushDir * 8f, ForceMode2D.Impulse);
        }

        if (spell.HasTag(SpellTag.PULL) && playerHealth != null)
        {
            var enemyRb = enemy.GetComponent<Rigidbody2D>();
            Vector2 toPlayer = ((Vector2)playerHealth.transform.position - (Vector2)enemy.transform.position).normalized;
            enemyRb?.AddForce(toPlayer * 10f, ForceMode2D.Impulse);
        }

        if (spell.HasTag(SpellTag.AOE_BURST))
        {
            Collider2D[] aoeHits = Physics2D.OverlapCircleAll(transform.position, 3f, ProjectileHandler.EnemyMask);
            foreach (var aoeHit in aoeHits)
            {
                var aooh = aoeHit.GetComponent<Health>();
                if (aooh != null && !aooh.IsDead && aoeHit.gameObject != enemy)
                    aooh.TakeDamage(spell.damage * 0.5f);
            }
        }

        var status = enemy.GetComponent<StatusEffectHandler>()
                     ?? enemy.AddComponent<StatusEffectHandler>();

        if (spell.HasTag(SpellTag.BURN))   status.ApplyBurn(spell.damage);
        if (spell.HasTag(SpellTag.FREEZE)) status.ApplyFreeze();
        if (spell.HasTag(SpellTag.SLOW))   status.ApplySlow();
        if (spell.HasTag(SpellTag.STUN))   status.ApplyStun();
        if (spell.HasTag(SpellTag.POISON)) status.ApplyPoison(spell.damage);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        hitCooldowns.Remove(other.gameObject);
    }
}
