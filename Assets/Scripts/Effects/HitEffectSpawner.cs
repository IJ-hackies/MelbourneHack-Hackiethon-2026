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
