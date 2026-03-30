using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Screen-space HUD: heart with health (top-left), status effect queue (bottom-left).
//
// Setup on the Player prefab:
//   1. Add this component.
//   2. Assign Heart, Burn, Poison, Bleed, Slow prefabs from Prefabs/Icons/ & Prefabs/UI/.
//   3. Create a TMP font asset from Assets/Fonts/alagard.ttf via
//      Window > TextMeshPro > Font Asset Creator, then assign it here.
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(PlayerStatusEffects))]
public class PlayerHUD : MonoBehaviour
{
    [Header("Prefabs (sprites are extracted at runtime)")]
    [SerializeField] private GameObject heartIconPrefab;
    [SerializeField] private GameObject burnIconPrefab;
    [SerializeField] private GameObject poisonIconPrefab;
    [SerializeField] private GameObject bleedIconPrefab;
    [SerializeField] private GameObject slowIconPrefab;

    [Header("Font — assign TMP font asset generated from Assets/Fonts/alagard.ttf")]
    [SerializeField] private TMP_FontAsset alagardFont;

    [Header("Status Row Position (canvas units at 1920×1080)")]
    [SerializeField] private Vector2 statusPosition = new Vector2(20f, 20f);

    // ── Layout (at 1920×1080 reference resolution) ────────────────────────────
    private const float IconSize    = 58f;
    private const float IconSpacing = 61f; // icon width + gap

    // ── Runtime references ────────────────────────────────────────────────────
    private Health              health;
    private PlayerStatusEffects status;
    private GameObject          canvasGO;

    // ── UI nodes ──────────────────────────────────────────────────────────────
    private RectTransform heartRT;
    private TMP_Text      healthText;
    private Image         healthBarFill;
    private RectTransform healthBarFillRT;
    private RectTransform statusContainer;

    private static Texture2D _glowTex; // cached soft-circle texture for status glows

    // ── Heartbeat ─────────────────────────────────────────────────────────────
    private float beatPhase;
    private float fastBeatTimer;
    private float healthFlashTimer;

    // ── Screen damage flash ───────────────────────────────────────────────────
    private Image screenFlashImage;
    private float screenFlashTimer;
    private const float ScreenFlashDuration = 0.25f;
    private const float ScreenFlashMaxAlpha = 0.02f;

    // ── Status slots ──────────────────────────────────────────────────────────
    private enum EffectType { Burn, Poison, Bleed, Slow }

    private class StatusSlot
    {
        public EffectType    type;
        public RectTransform rt;
        public TMP_Text      timerText;
        public float         targetX;
        public float         currentX;
        public float         lastRemaining;
        public float         flashTimer;
        public RawImage      glowImage; // pulsing glow behind the icon
    }

    private readonly List<StatusSlot> slots = new List<StatusSlot>();

    private Sprite spriteBurn, spritePoison, spriteBleed, spriteSlow;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        health = GetComponent<Health>();
        status = GetComponent<PlayerStatusEffects>();
    }

    private void Start()
    {
        BuildHUD();
        health.OnDamaged.AddListener(_ => TriggerDamageFlash());
        health.OnDamaged.AddListener(_ => SFXManager.Instance?.PlayPlayerHit());
    }

    private void OnDestroy()
    {
        if (canvasGO != null) Destroy(canvasGO);
    }

    private void TriggerDamageFlash()
    {
        fastBeatTimer    = 2f;
        healthFlashTimer = 0.45f;
        screenFlashTimer = ScreenFlashDuration;
    }

    // ── Canvas construction ───────────────────────────────────────────────────

    private void BuildHUD()
    {
        canvasGO = new GameObject("PlayerHUD_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Health bar row — bottom-center: [heart] [===bar===] ──────────────
        const float BarW       = 340f;
        const float BarH       = 22f;
        const float BarBottomY = 16f;
        const float HeartDim   = 44f;
        float barCenterY = BarBottomY + BarH * 0.5f; // 27 px from screen bottom

        // Heart container — also drives heartbeat scale animation
        heartRT = MakeRT("Heart", canvasGO.transform,
            anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0.5f),
            pos: new Vector2(-(BarW * 0.5f + 8f + HeartDim * 0.5f), barCenterY),
            size: new Vector2(HeartDim, HeartDim));

        var heartShadowRT    = MakeRT("HeartShadow", heartRT,
            anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0.5f, 0.5f),
            pos: new Vector2(3f, -3f), size: new Vector2(HeartDim + 8f, HeartDim + 8f));
        var heartShadowImg   = heartShadowRT.gameObject.AddComponent<Image>();
        heartShadowImg.sprite = ExtractSprite(heartIconPrefab);
        heartShadowImg.preserveAspect = true;
        heartShadowImg.color = new Color(0f, 0f, 0f, 0.6f);

        var heartMainRT      = MakeRT("HeartMain", heartRT,
            anchorMin: Vector2.zero, anchorMax: Vector2.one,
            pivot: new Vector2(0.5f, 0.5f),
            pos: Vector2.zero, size: Vector2.zero);
        var heartImg         = heartMainRT.gameObject.AddComponent<Image>();
        heartImg.sprite      = ExtractSprite(heartIconPrefab);
        heartImg.preserveAspect = true;

        // Bar drop shadow — flat rect, offset slightly for depth
        var barShadowRT      = MakeRT("BarShadow", canvasGO.transform,
            anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0.5f),
            pos: new Vector2(2f, barCenterY - 2f),
            size: new Vector2(BarW + 8f, BarH + 8f));
        var barShadowImg     = barShadowRT.gameObject.AddComponent<Image>();
        barShadowImg.color   = new Color(0f, 0f, 0f, 0.70f);
        barShadowImg.raycastTarget = false;

        // Bar outline — tight black border around the bar
        const float OutlinePx = 2f;
        var barOutlineRT     = MakeRT("BarOutline", canvasGO.transform,
            anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0.5f),
            pos: new Vector2(0f, barCenterY),
            size: new Vector2(BarW + OutlinePx * 2f, BarH + OutlinePx * 2f));
        var barOutlineImg    = barOutlineRT.gameObject.AddComponent<Image>();
        barOutlineImg.color  = new Color(0f, 0f, 0f, 1f);
        barOutlineImg.raycastTarget = false;

        // Bar container — flat rect mask; dark background visible in empty portion
        var barContainerRT   = MakeRT("BarContainer", canvasGO.transform,
            anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0.5f),
            pos: new Vector2(0f, barCenterY),
            size: new Vector2(BarW, BarH));
        var barContainerImg  = barContainerRT.gameObject.AddComponent<Image>();
        barContainerImg.color  = new Color(0.06f, 0.04f, 0.03f, 0.9f);
        barContainerImg.raycastTarget = false;
        var barMask          = barContainerRT.gameObject.AddComponent<Mask>();
        barMask.showMaskGraphic = true;

        // Bar fill — left-anchored child; anchorMax.x is set to health pct each frame
        healthBarFillRT      = MakeRT("BarFill", barContainerRT,
            Vector2.zero, Vector2.one, Vector2.zero,
            Vector2.zero, Vector2.zero);
        healthBarFillRT.anchorMin = Vector2.zero;
        healthBarFillRT.anchorMax = Vector2.one;   // starts full; updated each frame
        healthBarFillRT.offsetMin = Vector2.zero;
        healthBarFillRT.offsetMax = Vector2.zero;
        healthBarFill             = healthBarFillRT.gameObject.AddComponent<Image>();
        healthBarFill.color       = new Color(0.05f, 0.52f, 0.08f);
        healthBarFill.raycastTarget = false;

        // HP number — sibling of container (not masked), overlaid on the bar
        var healthTextRT     = MakeRT("HealthText", canvasGO.transform,
            anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
            pivot: new Vector2(0.5f, 0.5f),
            pos: new Vector2(0f, barCenterY),
            size: new Vector2(BarW, BarH));
        healthText           = healthTextRT.gameObject.AddComponent<TextMeshProUGUI>();
        healthText.font      = alagardFont;
        healthText.fontSize  = 17f;
        healthText.fontStyle = FontStyles.Bold;
        healthText.alignment = TextAlignmentOptions.Center;
        healthText.textWrappingMode = TextWrappingModes.NoWrap;
        healthText.overflowMode     = TextOverflowModes.Overflow;
        healthText.color            = Color.white;
        healthText.outlineWidth     = 0.4f;
        healthText.outlineColor     = new Color32(0, 0, 0, 230);
        healthText.raycastTarget    = false;

        // Status icon row — anchored to bottom-left of canvas
        statusContainer = MakeRT("StatusRow", canvasGO.transform,
            anchorMin: Vector2.zero, anchorMax: Vector2.zero,
            pivot: Vector2.zero,
            pos: statusPosition,
            size: new Vector2(IconSpacing * 4f, IconSize));

        // Cache effect sprites
        spriteBurn   = ExtractSprite(burnIconPrefab);
        spritePoison = ExtractSprite(poisonIconPrefab);
        spriteBleed  = ExtractSprite(bleedIconPrefab);
        spriteSlow   = ExtractSprite(slowIconPrefab);

        // Full-screen damage flash overlay
        var flashRT = MakeRT("DamageFlash", canvasGO.transform,
            anchorMin: Vector2.zero, anchorMax: Vector2.one,
            pivot: new Vector2(0.5f, 0.5f),
            pos: Vector2.zero, size: Vector2.zero);
        screenFlashImage               = flashRT.gameObject.AddComponent<Image>();
        screenFlashImage.color         = new Color(1f, 0f, 0f, 0f);
        screenFlashImage.raycastTarget = false;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (health == null || status == null) return;
        UpdateHeartbeat();
        UpdateHealthText();
        UpdateScreenFlash();
        SyncStatusSlots();
        AnimateSlots();
    }

    // ── Heartbeat animation ───────────────────────────────────────────────────

    private void UpdateHeartbeat()
    {
        if (fastBeatTimer > 0f) fastBeatTimer -= Time.deltaTime;

        float freq      = fastBeatTimer > 0f ? 2.6f : 0.65f;
        float amplitude = fastBeatTimer > 0f ? 0.09f : 0.05f;

        beatPhase += freq * Mathf.PI * 2f * Time.deltaTime;
        float pulse = 1f + amplitude * Mathf.Max(0f, Mathf.Sin(beatPhase));
        heartRT.localScale = new Vector3(pulse, pulse, 1f);
    }

    // ── Screen damage flash ───────────────────────────────────────────────────

    private void UpdateScreenFlash()
    {
        if (screenFlashImage == null) return;
        if (screenFlashTimer > 0f)
        {
            screenFlashTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(screenFlashTimer / ScreenFlashDuration);
            // Quadratic ease-out: bright spike at hit, fast fade — gives a "beat" feel
            float alpha = ScreenFlashMaxAlpha * (t * t);
            screenFlashImage.color = new Color(1f, 0f, 0f, alpha);
        }
        else
        {
            screenFlashImage.color = new Color(1f, 0f, 0f, 0f);
        }
    }

    // ── Health text ───────────────────────────────────────────────────────────

    private void UpdateHealthText()
    {
        float pct = health.Max > 0f ? Mathf.Clamp01(health.Current / health.Max) : 0f;
        healthText.text = Mathf.CeilToInt(health.Current).ToString();

        // Bar fill + colour — anchorMax.x drives width; flash white on damage
        if (healthBarFill != null)
        {
            healthBarFillRT.anchorMax = new Vector2(pct, 1f);
            float flashT = Mathf.Clamp01(healthFlashTimer / 0.45f);
            healthBarFill.color = Color.Lerp(HealthBarColor(pct), Color.white, flashT * flashT);
        }

        if (healthFlashTimer > 0f)
        {
            healthFlashTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(healthFlashTimer / 0.45f);
            healthText.color = Color.Lerp(Color.white, new Color(1f, 0.12f, 0.12f), t);
        }
        else
        {
            healthText.color = Color.white;
        }
    }

    // Dark green (full) → amber (half) → red (empty)
    private static Color HealthBarColor(float pct)
    {
        if (pct > 0.5f)
            return Color.Lerp(new Color(1.0f, 0.65f, 0.0f), new Color(0.05f, 0.52f, 0.08f), (pct - 0.5f) * 2f);
        return Color.Lerp(new Color(0.85f, 0.08f, 0.05f), new Color(1.0f, 0.65f, 0.0f), pct * 2f);
    }

    // Generates a white pill-shaped (fully rounded ends) 9-sliced sprite.
    // The left/right ends are perfect semicircles; the middle stretches cleanly.
    private static Sprite CreatePillSprite(float barHeightUnits)
    {
        const int ppu = 4; // pixels per canvas unit — enough detail for smooth curves
        int h = Mathf.Max(2, Mathf.RoundToInt(barHeightUnits * ppu));
        int r = Mathf.Min(h / 2, 2 * ppu); // 2-unit corner radius — subtle rounding only
        int w = r * 2 + 4;            // caps + 4px stretchable centre
        var tex        = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var pixels     = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float cx   = Mathf.Clamp(x, r, w - 1 - r);
            float cy   = Mathf.Clamp(y, r, h - 1 - r);
            float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float a    = Mathf.Clamp01(r - dist + 0.5f);
            pixels[y * w + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
            ppu, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
    }

    // ── Status slot sync ──────────────────────────────────────────────────────

    private void SyncStatusSlots()
    {
        SyncSlot(EffectType.Burn,   status.IsBurning,  status.BurnRemaining);
        SyncSlot(EffectType.Poison, status.IsPoisoned, status.PoisonRemaining);
        SyncSlot(EffectType.Bleed,  status.IsBleeding, status.BleedRemaining);

        // Slow: timed gives countdown, zone gives no countdown (pass -1)
        bool  showSlow      = status.IsZoneSlowed || status.IsTimedSlowed;
        float slowRemaining = status.IsTimedSlowed ? status.TimedSlowRemaining : -1f;
        SyncSlot(EffectType.Slow, showSlow, slowRemaining);
    }

    private void SyncSlot(EffectType type, bool isActive, float remaining)
    {
        StatusSlot slot = FindSlot(type);

        if (!isActive)
        {
            if (slot != null) RemoveSlot(slot);
            return;
        }

        if (slot == null)
            slot = AddSlot(type);

        // Detect timer refresh (remaining jumped up = reapplied)
        if (remaining > 0f && slot.lastRemaining > 0f && remaining > slot.lastRemaining + 0.5f)
            slot.flashTimer = 0.4f;

        slot.lastRemaining = remaining;

        // Timer text
        if (remaining < 0f)
        {
            slot.timerText.text  = "";
        }
        else
        {
            int secs = Mathf.CeilToInt(Mathf.Max(0f, remaining));
            slot.timerText.text = secs > 0 ? secs.ToString() : "";
        }

        // Flash colour
        if (slot.flashTimer > 0f)
        {
            slot.flashTimer -= Time.deltaTime;
            float t = slot.flashTimer / 0.4f;
            slot.timerText.color = Color.Lerp(Color.white, new Color(1f, 0.12f, 0.12f), t);
        }
        else
        {
            slot.timerText.color = Color.white;
        }
    }

    // ── Slot lifecycle ────────────────────────────────────────────────────────

    private StatusSlot FindSlot(EffectType type)
    {
        foreach (var s in slots) if (s.type == type) return s;
        return null;
    }

    private StatusSlot AddSlot(EffectType type)
    {
        float startX = slots.Count * IconSpacing;

        // Container — no image component; sibling order controls depth: glow → icon → timer
        var rt = MakeRT($"Slot_{type}", statusContainer,
            anchorMin: Vector2.zero, anchorMax: Vector2.zero,
            pivot: Vector2.zero,
            pos: new Vector2(startX, 0f),
            size: new Vector2(IconSize, IconSize));

        // Glow backdrop — soft radial gradient, element colour, rendered behind icon
        var glowRT     = MakeRT("Glow", rt,
            anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
            pivot: Vector2.one * 0.5f,
            pos: Vector2.zero, size: new Vector2(IconSize * 1.85f, IconSize * 1.85f));
        var glowImg    = glowRT.gameObject.AddComponent<RawImage>();
        glowImg.texture = GetGlowTexture();
        glowImg.color   = GlowColorFor(type);

        // Icon image on top of glow
        var iconRT     = MakeRT("Icon", rt,
            anchorMin: Vector2.zero, anchorMax: Vector2.one,
            pivot: Vector2.one * 0.5f,
            pos: Vector2.zero, size: Vector2.zero);
        var img        = iconRT.gameObject.AddComponent<Image>();
        img.sprite     = SpriteFor(type);
        img.preserveAspect = true;

        // Timer text on top of everything
        var timerRT = MakeRT("Timer", rt,
            anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0.5f, 0.5f),
            pos: new Vector2(TextOffsetXFor(type), 0f), size: new Vector2(IconSize, IconSize));

        var tmp          = timerRT.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font         = alagardFont;
        tmp.fontSize     = 26f;
        tmp.fontStyle    = FontStyles.Bold;
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color        = Color.white;
        tmp.outlineWidth = 0.35f;
        tmp.outlineColor = new Color32(0, 0, 0, 220);

        var slot = new StatusSlot
        {
            type       = type,
            rt         = rt,
            timerText  = tmp,
            targetX    = startX,
            currentX   = startX,
            glowImage  = glowImg,
        };
        slots.Add(slot);
        RecalcTargets();
        return slot;
    }

    private void RemoveSlot(StatusSlot slot)
    {
        if (slot.rt != null) Destroy(slot.rt.gameObject);
        slots.Remove(slot);
        RecalcTargets();
    }

    private void RecalcTargets()
    {
        for (int i = 0; i < slots.Count; i++)
            slots[i].targetX = i * IconSpacing;
    }

    private void AnimateSlots()
    {
        foreach (var slot in slots)
        {
            slot.currentX = slot.targetX;
            slot.rt.anchoredPosition = new Vector2(slot.currentX, 0f);

            // Gently pulse the glow alpha so icons feel alive
            if (slot.glowImage != null)
            {
                float pulse = 0.45f + 0.28f * Mathf.Sin(Time.time * Mathf.PI * 1.5f + (float)slot.type * 0.9f);
                Color c = slot.glowImage.color;
                c.a = pulse;
                slot.glowImage.color = c;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Horizontal nudge (negative = left) to visually centre the number on each sprite.
    private static float TextOffsetXFor(EffectType type) => type switch
    {
        EffectType.Bleed  => -9f,
        EffectType.Poison => -9f,
        EffectType.Burn   => -5f,
        _                 => 0f,
    };

    private Sprite SpriteFor(EffectType type) => type switch
    {
        EffectType.Burn   => spriteBurn,
        EffectType.Poison => spritePoison,
        EffectType.Bleed  => spriteBleed,
        EffectType.Slow   => spriteSlow,
        _                 => null,
    };

    private static Sprite ExtractSprite(GameObject prefab) =>
        prefab != null ? prefab.GetComponent<SpriteRenderer>()?.sprite : null;

    // Soft radial gradient texture used as the glow backdrop on status icons.
    // Generated once and cached — avoids per-slot allocations.
    private static Texture2D GetGlowTexture()
    {
        if (_glowTex != null) return _glowTex;
        const int size = 32;
        _glowTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dist = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
            float a    = Mathf.Clamp01(1f - dist);
            a          = a * a; // quadratic falloff — bright centre, soft transparent edge
            _glowTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        _glowTex.Apply();
        return _glowTex;
    }

    // Element colour for each status effect glow.
    private static Color GlowColorFor(EffectType type) => type switch
    {
        EffectType.Burn   => new Color(1.0f, 0.38f, 0.0f, 0.7f),  // orange
        EffectType.Poison => new Color(0.22f, 0.85f, 0.1f, 0.7f), // green
        EffectType.Bleed  => new Color(0.85f, 0.08f, 0.08f, 0.7f),// red
        EffectType.Slow   => new Color(0.28f, 0.62f, 1.0f, 0.7f), // ice blue
        _                 => new Color(1f, 1f, 1f, 0.4f),
    };

    private static RectTransform MakeRT(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt        = go.AddComponent<RectTransform>();
        rt.anchorMin  = anchorMin;
        rt.anchorMax  = anchorMax;
        rt.pivot      = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta  = size;
        return rt;
    }
}
