using UnityEngine;

/// <summary>
/// Base class for all modular projectile behavior components.
/// ProjectileHandler adds these dynamically at Init time based on SpellTags.
/// Each behavior owns its own Update/FixedUpdate logic.
/// </summary>
public abstract class ProjectileBehaviorBase : MonoBehaviour
{
    protected ProjectileContext ctx;

    /// <summary>Called by ProjectileHandler immediately after AddComponent.</summary>
    public void Initialize(ProjectileContext context)
    {
        ctx = context;
        OnInitialized();
    }

    /// <summary>Override to run setup logic after ctx is assigned.</summary>
    protected virtual void OnInitialized() { }

    /// <summary>
    /// Called when the projectile hits an enemy (after damage/status are applied).
    /// Return true to suppress the default destroy-after-hit logic in ProjectileHandler.
    /// </summary>
    public virtual bool OnHitEnemy(GameObject enemy) => false;
}
