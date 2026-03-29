using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Theme-aware dungeon ambience: world-space camera-local particles + screen-space vignette + tint.
///
/// Setup: Attach to any persistent GameObject in the Dungeon scene.
///        Automatically hooks into FloorAssembler.OnFloorReady to swap themes.
/// </summary>
public class DungeonAmbience : MonoBehaviour
{
    private Camera         mainCam;
    private ParticleSystem ambientPS;
    private Image          vignetteImage;
    private Image          tintImage;
    private Texture2D      vignetteTexture;
    private string         currentTheme = "";

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Start()
    {
        mainCam = Camera.main;
        BuildCanvas();
        BuildParticles();
        FloorAssembler.OnFloorReady += OnFloorReady;

        var fa = FindAnyObjectByType<FloorAssembler>();
        ApplyTheme(fa != null && !string.IsNullOrEmpty(fa.activeTilesetId)
            ? fa.activeTilesetId : "dungeon");
    }

    private void OnDestroy()
    {
        FloorAssembler.OnFloorReady -= OnFloorReady;
        if (vignetteTexture != null) Destroy(vignetteTexture);
    }

    private void Update()
    {
        // Keep particle emitter centred on camera each frame
        if (mainCam != null && ambientPS != null)
            ambientPS.transform.position = mainCam.transform.position;
    }

    private void OnFloorReady(Vector3 _)
    {
        var fa = FindAnyObjectByType<FloorAssembler>();
        ApplyTheme(fa != null ? fa.activeTilesetId : "dungeon");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CANVAS  — vignette + theme tint
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildCanvas()
    {
        var cGO    = new GameObject("AmbienceCanvas");
        var canvas = cGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5; // above world, well below HUD (90)

        var scaler = cGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        cGO.AddComponent<GraphicRaycaster>();

        // Very faint theme colour tint (rendered first, behind vignette)
        var tintGO = new GameObject("ThemeTint");
        tintGO.transform.SetParent(cGO.transform, false);
        tintImage = tintGO.AddComponent<Image>();
        tintImage.raycastTarget = false;
        Stretch(tintImage.rectTransform);
        tintImage.color = Color.clear;

        // Radial vignette — programmatically generated white-to-transparent texture,
        // tinted to theme colour via Image.color
        var vigGO = new GameObject("Vignette");
        vigGO.transform.SetParent(cGO.transform, false);
        vignetteImage = vigGO.AddComponent<Image>();
        vignetteImage.raycastTarget = false;
        Stretch(vignetteImage.rectTransform);

        vignetteTexture = GenerateVignetteTexture(256, 256);
        vignetteImage.sprite = Sprite.Create(
            vignetteTexture,
            new Rect(0, 0, 256, 256),
            new Vector2(0.5f, 0.5f), 100f);
        vignetteImage.color = Color.clear;
    }

    private static Texture2D GenerateVignetteTexture(int w, int h)
    {
        var tex        = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels     = new Color[w * h];

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float nx = (x / (float)(w - 1)) * 2f - 1f; // -1 … +1
            float ny = (y / (float)(h - 1)) * 2f - 1f;
            float d  = Mathf.Sqrt(nx * nx + ny * ny);
            float a  = Mathf.Clamp01((d - 0.55f) / 0.65f);
            a *= a; // quadratic — soft centre, stronger rim
            pixels[y * w + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  PARTICLES  — world-space, follows camera
    // ═════════════════════════════════════════════════════════════════════════

    private void BuildParticles()
    {
        var psGO  = new GameObject("AmbientParticles");
        ambientPS = psGO.AddComponent<ParticleSystem>();
        ambientPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr = psGO.GetComponent<ParticleSystemRenderer>();
        psr.sortingLayerName = "Entities";
        psr.sortingOrder     = 60;
        psr.renderMode       = ParticleSystemRenderMode.Billboard;
        ApplyParticleMaterial(psr);

        var main = ambientPS.main;
        main.loop            = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var shape = ambientPS.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        // Slightly larger than camera view (ortho 8 × aspect ~1.78 ≈ 28.5 × 18 world units)
        shape.scale = new Vector3(34f, 22f, 0.1f);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  THEME APPLICATION
    // ═════════════════════════════════════════════════════════════════════════

    private void ApplyTheme(string theme)
    {
        if (currentTheme == theme) return;
        currentTheme = theme;

        var c = GetConfig(theme);

        // Tint — VERY faint
        tintImage.color = new Color(c.tintR, c.tintG, c.tintB, 0.04f);

        // Vignette — Image.color tints the white texture
        vignetteImage.color = new Color(c.vigR, c.vigG, c.vigB, 0.25f);

        // Particles
        ambientPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ambientPS.main;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(c.lifeMin, c.lifeMax);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(c.speedMin, c.speedMax);
        main.startSize       = new ParticleSystem.MinMaxCurve(c.sizeMin, c.sizeMax);
        main.startColor      = new ParticleSystem.MinMaxGradient(c.colA, c.colB);
        main.gravityModifier = c.gravity;
        main.maxParticles    = c.maxParticles;

        var emission = ambientPS.emission;
        emission.rateOverTime = c.rate;

        var vol = ambientPS.velocityOverLifetime;
        vol.enabled = true;
        vol.x = new ParticleSystem.MinMaxCurve(c.velXMin, c.velXMax);
        vol.y = new ParticleSystem.MinMaxCurve(c.velYMin, c.velYMax);
        vol.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        // Fade in and out over each particle's lifetime
        var col = ambientPS.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f),  new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        ambientPS.Play();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  THEME DATA TABLE
    // ═════════════════════════════════════════════════════════════════════════

    private struct Cfg
    {
        public float tintR, tintG, tintB;           // screen tint colour (applied at alpha 0.04)
        public float vigR,  vigG,  vigB;            // vignette edge colour (applied at alpha 0.50)
        public float lifeMin, lifeMax;
        public float speedMin, speedMax;
        public float sizeMin,  sizeMax;
        public Color colA, colB;                    // particle start colour range
        public float gravity;                       // negative = rise, positive = fall
        public int   maxParticles;
        public float rate;
        public float velXMin, velXMax;
        public float velYMin, velYMax;
    }

    private static Cfg GetConfig(string theme) => theme switch
    {
        "dungeon" => new Cfg {
            tintR=0.15f, tintG=0.08f, tintB=0.02f,
            vigR=0.10f,  vigG=0.05f,  vigB=0.00f,
            lifeMin=4f, lifeMax=8f, speedMin=0.02f, speedMax=0.08f,
            sizeMin=0.024f, sizeMax=0.072f,
            colA=new Color(1.00f,0.85f,0.50f,0.80f), colB=new Color(1.00f,0.70f,0.30f,0.55f),
            gravity=0f, maxParticles=72, rate=9f,
            velXMin=-0.05f, velXMax=0.05f, velYMin=-0.02f, velYMax=0.04f,
        },
        "forest" => new Cfg {
            tintR=0.04f, tintG=0.12f, tintB=0.02f,
            vigR=0.02f,  vigG=0.10f,  vigB=0.01f,
            lifeMin=5f, lifeMax=9f, speedMin=0.02f, speedMax=0.07f,
            sizeMin=0.024f, sizeMax=0.084f,
            colA=new Color(0.40f,0.90f,0.20f,0.80f), colB=new Color(0.60f,1.00f,0.40f,0.55f),
            gravity=-0.01f, maxParticles=78, rate=10.5f,
            velXMin=-0.06f, velXMax=0.06f, velYMin=-0.04f, velYMax=0.01f,
        },
        "frozen" => new Cfg {
            tintR=0.04f, tintG=0.06f, tintB=0.14f,
            vigR=0.10f,  vigG=0.15f,  vigB=0.30f,
            lifeMin=5f, lifeMax=10f, speedMin=0.03f, speedMax=0.09f,
            sizeMin=0.018f, sizeMax=0.06f,
            colA=new Color(0.70f,0.90f,1.00f,0.80f), colB=new Color(0.90f,0.97f,1.00f,0.55f),
            gravity=0.015f, maxParticles=84, rate=12f,
            velXMin=-0.04f, velXMax=0.04f, velYMin=-0.07f, velYMax=-0.01f,
        },
        "flames" => new Cfg {
            tintR=0.15f, tintG=0.04f, tintB=0.00f,
            vigR=0.30f,  vigG=0.05f,  vigB=0.00f,
            lifeMin=2f, lifeMax=5f, speedMin=0.05f, speedMax=0.15f,
            sizeMin=0.024f, sizeMax=0.066f,
            colA=new Color(1.00f,0.50f,0.10f,0.80f), colB=new Color(1.00f,0.30f,0.05f,0.55f),
            gravity=-0.06f, maxParticles=84, rate=15f,
            velXMin=-0.05f, velXMax=0.05f, velYMin=0.05f, velYMax=0.18f,
        },
        "acid" => new Cfg {
            tintR=0.02f, tintG=0.12f, tintB=0.01f,
            vigR=0.05f,  vigG=0.20f,  vigB=0.02f,
            lifeMin=3f, lifeMax=6f, speedMin=0.03f, speedMax=0.10f,
            sizeMin=0.030f, sizeMax=0.072f,
            colA=new Color(0.30f,1.00f,0.10f,0.80f), colB=new Color(0.50f,0.90f,0.20f,0.55f),
            gravity=-0.04f, maxParticles=72, rate=12f,
            velXMin=-0.04f, velXMax=0.04f, velYMin=0.03f, velYMax=0.12f,
        },
        "ocean" => new Cfg {
            tintR=0.02f, tintG=0.06f, tintB=0.14f,
            vigR=0.05f,  vigG=0.10f,  vigB=0.25f,
            lifeMin=6f, lifeMax=11f, speedMin=0.01f, speedMax=0.06f,
            sizeMin=0.030f, sizeMax=0.084f,
            colA=new Color(0.40f,0.80f,1.00f,0.80f), colB=new Color(0.80f,0.95f,1.00f,0.55f),
            gravity=-0.015f, maxParticles=66, rate=7.5f,
            velXMin=-0.04f, velXMax=0.04f, velYMin=-0.01f, velYMax=0.04f,
        },
        "rocky" => new Cfg {
            tintR=0.06f, tintG=0.05f, tintB=0.04f,
            vigR=0.10f,  vigG=0.08f,  vigB=0.06f,
            lifeMin=4f, lifeMax=8f, speedMin=0.02f, speedMax=0.07f,
            sizeMin=0.012f, sizeMax=0.048f,
            colA=new Color(0.75f,0.70f,0.65f,0.80f), colB=new Color(0.60f,0.55f,0.50f,0.55f),
            gravity=0.02f, maxParticles=66, rate=9f,
            velXMin=-0.03f, velXMax=0.03f, velYMin=-0.06f, velYMax=-0.01f,
        },
        "techy" => new Cfg {
            tintR=0.01f, tintG=0.06f, tintB=0.10f,
            vigR=0.05f,  vigG=0.15f,  vigB=0.25f,
            lifeMin=1.5f, lifeMax=4f, speedMin=0.05f, speedMax=0.18f,
            sizeMin=0.012f, sizeMax=0.042f,
            colA=new Color(0.20f,1.00f,0.90f,0.80f), colB=new Color(0.40f,0.90f,1.00f,0.55f),
            gravity=0f, maxParticles=78, rate=15f,
            velXMin=-0.12f, velXMax=0.12f, velYMin=-0.06f, velYMax=0.06f,
        },
        "bubblegum" => new Cfg {
            tintR=0.10f, tintG=0.02f, tintB=0.08f,
            vigR=0.20f,  vigG=0.05f,  vigB=0.18f,
            lifeMin=4f, lifeMax=8f, speedMin=0.02f, speedMax=0.08f,
            sizeMin=0.024f, sizeMax=0.078f,
            colA=new Color(1.00f,0.50f,0.80f,0.80f), colB=new Color(0.80f,0.40f,1.00f,0.55f),
            gravity=-0.01f, maxParticles=78, rate=10.5f,
            velXMin=-0.05f, velXMax=0.05f, velYMin=-0.02f, velYMax=0.05f,
        },
        "honey" => new Cfg {
            tintR=0.12f, tintG=0.08f, tintB=0.00f,
            vigR=0.25f,  vigG=0.15f,  vigB=0.00f,
            lifeMin=5f, lifeMax=9f, speedMin=0.02f, speedMax=0.06f,
            sizeMin=0.024f, sizeMax=0.072f,
            colA=new Color(1.00f,0.85f,0.10f,0.80f), colB=new Color(1.00f,0.70f,0.20f,0.55f),
            gravity=0.02f, maxParticles=66, rate=9f,
            velXMin=-0.03f, velXMax=0.03f, velYMin=-0.06f, velYMax=-0.01f,
        },
        _ => new Cfg {
            tintR=0f, tintG=0f, tintB=0f, vigR=0f, vigG=0f, vigB=0f,
            lifeMin=4f, lifeMax=8f, speedMin=0.02f, speedMax=0.08f,
            sizeMin=0.024f, sizeMax=0.072f,
            colA=new Color(1f,1f,1f,0.5f), colB=new Color(0.8f,0.8f,0.8f,0.3f),
            gravity=0f, maxParticles=72, rate=9f,
            velXMin=-0.05f, velXMax=0.05f, velYMin=-0.02f, velYMax=0.02f,
        },
    };

    // ═════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void ApplyParticleMaterial(ParticleSystemRenderer psr)
    {
        string[] candidates = {
            "Universal Render Pipeline/Particles/Unlit",
            "Particles/Standard Unlit",
            "Legacy Shaders/Particles/Alpha Blended Premultiply",
            "Sprites/Default",
        };
        Shader shader = null;
        foreach (var s in candidates) { shader = Shader.Find(s); if (shader != null) break; }
        if (shader == null) return;
        var mat = new Material(shader) { color = Color.white };
        var knob = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
        if (knob != null) mat.mainTexture = knob.texture;
        psr.material = mat;
    }
}
