using UnityEngine;
using UnityEditor;

/// <summary>
/// Tools > Grimoire > Create Spell Projectile Prefab
/// Generates a ready-to-use projectile prefab at Assets/Prefabs/Spells/SpellProjectile.prefab.
/// Assign it to SpellExecutor's Projectile Prefab field.
/// </summary>
public static class CreateSpellProjectile
{
    [MenuItem("Tools/Grimoire/Create Spell Projectile Prefab")]
    public static void Create()
    {
        // --- Root object ---
        var go = new GameObject("SpellProjectile");
        go.layer = LayerMask.NameToLayer("Hitbox");

        // --- Physics ---
        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // --- Trigger collider (enemy hit detection) ---
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.15f;

        // --- Core visual: glowing orb sprite ---
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        sr.color = new Color(0.4f, 0.85f, 1f, 1f); // cyan — tint per element at runtime later
        sr.sortingLayerName = "Default";
        sr.sortingOrder = 5;
        go.transform.localScale = Vector3.one * 0.35f;

        // --- Particle trail (child object) ---
        var trailGO = new GameObject("Trail");
        trailGO.transform.SetParent(go.transform, false);

        var ps = trailGO.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        trailGO.GetComponent<ParticleSystemRenderer>().material =
            new Material(urp != null ? urp : fallback);

        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0f, 0.5f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
                                   new Color(0.3f, 0.8f, 1f),
                                   new Color(0.8f, 0.9f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World; // trail stays behind
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = 60f;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.05f;

        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.4f, 0.9f, 1f), 0f),
                    new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

        ps.Play();

        // --- Spell behavior ---
        go.AddComponent<ProjectileHandler>();

        // --- Save prefab ---
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Spells"))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Spells");

        const string path = "Assets/Prefabs/Spells/SpellProjectile.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out bool success);
        Object.DestroyImmediate(go);

        if (success)
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = prefab;
            Debug.Log($"[Grimoire] SpellProjectile prefab created at {path}");
        }
        else
        {
            Debug.LogError("[Grimoire] Failed to create SpellProjectile prefab.");
        }
    }
}
