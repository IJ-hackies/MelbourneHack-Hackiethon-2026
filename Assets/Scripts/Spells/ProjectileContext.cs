using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared read-only state passed to all projectile behavior components.
/// Created once by ProjectileHandler.Init; do not modify after initialization.
/// Use HasTag() instead of accessing EffectiveTags directly — it reflects PROBABILITY resolution.
/// </summary>
public class ProjectileContext
{
    public SpellData Spell { get; }

    /// <summary>
    /// Active tags after PROBABILITY resolution. May differ from Spell.tags.
    /// All behaviors must read this, not Spell.tags directly.
    /// </summary>
    public HashSet<SpellTag> EffectiveTags { get; }

    public Rigidbody2D Rb { get; }
    public Health PlayerHealth { get; }
    public Transform CasterTransform { get; }
    public Vector2 InitialDirection { get; }
    public ProjectileHandler Handler { get; }

    public ProjectileContext(
        SpellData spell,
        HashSet<SpellTag> effectiveTags,
        Rigidbody2D rb,
        Health playerHealth,
        Transform casterTransform,
        Vector2 initialDir,
        ProjectileHandler handler)
    {
        Spell            = spell;
        EffectiveTags    = effectiveTags;
        Rb               = rb;
        PlayerHealth     = playerHealth;
        CasterTransform  = casterTransform;
        InitialDirection = initialDir;
        Handler          = handler;
    }

    public bool HasTag(SpellTag tag) => EffectiveTags.Contains(tag);
}
