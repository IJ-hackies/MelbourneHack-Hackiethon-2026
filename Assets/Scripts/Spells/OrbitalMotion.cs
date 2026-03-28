using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attached to an orbital projectile spawned by SpellExecutor.
/// Orbits the parent (player) and deals damage to enemies on contact.
/// </summary>
public class OrbitalMotion : MonoBehaviour
{
    private SpellData spell;
    private float angle;
    private const float radius = 1.5f;
    private const float orbitSpeed = 200f; // degrees per second
    private const float damageCooldown = 0.6f;

    // Per-enemy cooldown so the orbital doesn't deal damage every frame
    private readonly Dictionary<GameObject, float> hitCooldowns = new();

    public void Init(SpellData spellData, int orbitIndex)
    {
        spell = spellData;
        angle = orbitIndex * 120f; // space up to 3 orbitals 120° apart
    }

    private void Update()
    {
        if (spell == null) return;

        angle += orbitSpeed * Time.deltaTime;
        float rad = angle * Mathf.Deg2Rad;
        transform.localPosition = new Vector2(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!other.CompareTag("Enemy")) return;

        GameObject enemy = other.gameObject;
        float now = Time.time;

        if (hitCooldowns.TryGetValue(enemy, out float lastHit) && now - lastHit < damageCooldown)
            return;

        var h = other.GetComponent<Health>();
        if (h == null || h.IsDead) return;

        h.TakeDamage(spell.damage);
        hitCooldowns[enemy] = now;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        hitCooldowns.Remove(other.gameObject);
    }
}
