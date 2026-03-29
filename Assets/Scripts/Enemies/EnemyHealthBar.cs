using UnityEngine;

/// <summary>
/// Procedural world-space health bar for enemies.
/// Auto-attached by EnemyBase.Awake() — do NOT manually add this component.
///
/// Bar theming is configured via the "Bar Color" field on the EnemyBase component.
/// Leave it at (0,0,0,0) to use the default green→yellow→red health gradient.
/// Set it to a tinted colour (with alpha > 0) for elemental / special enemies.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Health))]
public class EnemyHealthBar : MonoBehaviour
{
    // ── Layout (world units) ──────────────────────────────────────────────────
    private const float BarWidth      = 0.72f;
    private const float BarHeight     = 0.07f;
    private const float SegGap        = 0.02f;
    private const int   SegCount      = 3;
    private const float BgPadX        = 0.03f;
    private const float BgPadY        = 0.025f;
    private const float HeadOffset    = 0.1f;   // above sprite top

    // ── Timing ────────────────────────────────────────────────────────────────
    private const float ShowDuration   = 2.0f;
    private const float FadeOutTime    = 0.5f;
    private const float FlashDuration  = 0.28f;
    private const float BounceDuration = 0.25f;
    private const float BounceAmount   = 0.12f;  // 12% scale overshoot

    // ── Default palette ───────────────────────────────────────────────────────
    private static readonly Color HealthHigh   = new Color(0.27f, 0.82f, 0.27f, 1f); // green
    private static readonly Color HealthMid    = new Color(0.90f, 0.75f, 0.13f, 1f); // yellow
    private static readonly Color HealthLow    = new Color(0.87f, 0.15f, 0.15f, 1f); // red
    private static readonly Color EmptyCol     = new Color(0.14f, 0.14f, 0.14f, 1f);
    private static readonly Color BgCol        = new Color(0.05f, 0.05f, 0.05f, 1f);
    private static readonly Color FlashDmgCol  = new Color(0.95f, 0.12f, 0.12f, 1f);
    private static readonly Color FlashHealCol = new Color(0.20f, 0.95f, 0.20f, 1f);

    // ── Runtime refs ─────────────────────────────────────────────────────────
    private Health         _health;
    private SpriteRenderer _sourceSR;
    private Color          _barColor;   // copied from EnemyBase.BarColor at Awake

    private Transform      _root;
    private SpriteRenderer _bgSR;
    private SpriteRenderer[] _segSRs = new SpriteRenderer[SegCount];

    // ── State ─────────────────────────────────────────────────────────────────
    private float _prevHp;
    private float _showTimer;
    private float _fadeAlpha;
    private float _flashTimer;
    private Color _flashColor;
    private float _bounceTimer;

    // ── Shared 1×1 sprite ─────────────────────────────────────────────────────
    private static Texture2D _pixTex;
    private static Sprite    _pixSprite;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _health   = GetComponent<Health>();
        _sourceSR = GetComponent<SpriteRenderer>();
        // _barColor is set by EnemyBase immediately after AddComponent via SetThemeColor()
        BuildBar();
        _prevHp = _health.Max;
        SetFadeAlpha(0f);
    }

    /// <summary>Called by EnemyBase right after AddComponent — sets the per-enemy bar colour.</summary>
    public void SetThemeColor(Color c) => _barColor = c;

    private void Start()
    {
        _health.OnDeath.AddListener(OnEnemyDeath);
    }

    private void OnDestroy()
    {
        // barRoot is a child, destroyed with the enemy — this is just belt-and-suspenders
        if (_root != null) Destroy(_root.gameObject);
    }

    private void OnEnemyDeath()
    {
        _showTimer = 0f;
        SetFadeAlpha(0f);
    }

    // ── LateUpdate ────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (_health == null) return;

        if (_health.IsDead)
        {
            if (_fadeAlpha > 0f) SetFadeAlpha(0f);
            return;
        }

        // ── Detect HP changes (damage & regen heals) ─────────────────────────
        float curr = _health.Current;
        if (curr < _prevHp - 0.01f)
            Activate(DamageFlash());
        else if (curr > _prevHp + 0.01f)
            Activate(HealFlash());
        _prevHp = curr;

        // ── Timers ────────────────────────────────────────────────────────────
        _flashTimer  = Mathf.Max(0f, _flashTimer  - Time.deltaTime);
        _bounceTimer = Mathf.Max(0f, _bounceTimer - Time.deltaTime);

        if (_showTimer > 0f)
        {
            _showTimer -= Time.deltaTime;
            SetFadeAlpha(1f);
        }
        else
        {
            SetFadeAlpha(Mathf.MoveTowards(_fadeAlpha, 0f, Time.deltaTime / FadeOutTime));
        }

        if (_fadeAlpha < 0.01f)
        {
            SetFadeAlpha(0f);
            return;
        }

        UpdateTransform();
        ApplyColors();
    }

    // ── Activation ────────────────────────────────────────────────────────────

    private void Activate(Color flashCol)
    {
        _showTimer   = ShowDuration;
        _flashTimer  = FlashDuration;
        _flashColor  = flashCol;
        _bounceTimer = BounceDuration;
    }

    // ── Transform ─────────────────────────────────────────────────────────────

    private void UpdateTransform()
    {
        // World position: top of sprite bounds + small gap
        float worldTop = _sourceSR.sprite != null
            ? _sourceSR.bounds.max.y
            : transform.position.y + 0.5f;

        _root.position = new Vector3(
            transform.position.x,
            worldTop + HeadOffset,
            transform.position.z - 0.01f);

        // Cancel parent scale so the bar is always the same world size
        Vector3 ls = transform.lossyScale;
        float sx = ls.x != 0f ? 1f / ls.x : 1f;
        float sy = ls.y != 0f ? 1f / ls.y : 1f;

        // Bounce: sin arc peaks at mid-duration
        float bounce = 0f;
        if (_bounceTimer > 0f)
        {
            float t = 1f - _bounceTimer / BounceDuration;
            bounce = BounceAmount * Mathf.Sin(t * Mathf.PI);
        }
        float boost = 1f + bounce;
        _root.localScale = new Vector3(sx * boost, sy * boost, 1f);

        // Keep sorting above the animated sprite (YSorter updates sourceSR each LateUpdate)
        int baseOrder = _sourceSR.sortingOrder + 3;
        _bgSR.sortingOrder = baseOrder;
        for (int i = 0; i < SegCount; i++)
            _segSRs[i].sortingOrder = baseOrder + 1;
    }

    // ── Colours ───────────────────────────────────────────────────────────────

    private void ApplyColors()
    {
        float ratio  = _health.Max > 0f ? Mathf.Clamp01(_health.Current / _health.Max) : 0f;
        float baseA  = UseCustomColor ? _barColor.a : 1f;
        float alpha  = baseA * _fadeAlpha;
        // Subtle whole-bar tint — capped at 20% blend so it reads as a hint, not a blast
        float flashT = (_flashTimer / FlashDuration) * 0.20f;

        for (int i = 0; i < SegCount; i++)
        {
            bool  filled = ratio > (float)i / SegCount;
            Color baseC  = filled ? SegmentColor(i) : EmptyCol;
            Color c      = Color.Lerp(baseC, _flashColor, flashT);
            c.a = alpha;
            _segSRs[i].color = c;
        }

        Color bg = Color.Lerp(BgCol, _flashColor, flashT);
        bg.a = alpha;
        _bgSR.color = bg;
    }

    private void SetFadeAlpha(float a)
    {
        _fadeAlpha = a;
        bool on = a > 0.01f;
        if (_bgSR != null) _bgSR.enabled = on;
        for (int i = 0; i < SegCount; i++)
            if (_segSRs[i] != null) _segSRs[i].enabled = on;
    }

    // ── Theme helpers ─────────────────────────────────────────────────────────

    private bool UseCustomColor => _barColor.a > 0.01f;

    // Each segment has its own permanent shade.
    // i=0 (leftmost, last to empty) = darkest/most critical
    // i=1 (middle)                  = base colour
    // i=2 (rightmost, first to empty) = brightest/healthiest
    private Color SegmentColor(int i)
    {
        if (!UseCustomColor)
        {
            // Default gradient: red → yellow → green, fixed per segment
            if (i == 0) return HealthLow;
            if (i == 1) return HealthMid;
            return HealthHigh;
        }

        // Derive three shades from barColor via HSV
        Color.RGBToHSV(_barColor, out float h, out float s, out float v);
        float vShift = (i - 1) * 0.18f;  // -0.18 / 0 / +0.18
        float sShift = (1 - i) * 0.10f;  // +0.10 / 0  / -0.10 (slightly punchier at low end)
        return Color.HSVToRGB(h,
            Mathf.Clamp01(s + sShift),
            Mathf.Clamp01(v + vShift));
    }

    private Color DamageFlash()
    {
        if (!UseCustomColor) return FlashDmgCol;
        // Darken & saturate the enemy's bar colour
        Color.RGBToHSV(_barColor, out float h, out float s, out float v);
        return Color.HSVToRGB(h, Mathf.Min(1f, s + 0.20f), Mathf.Max(0.1f, v - 0.30f));
    }

    private Color HealFlash()
    {
        if (!UseCustomColor) return FlashHealCol;
        // Brighten & desaturate the enemy's bar colour
        Color.RGBToHSV(_barColor, out float h, out float s, out float v);
        return Color.HSVToRGB(h, Mathf.Max(0f, s - 0.25f), Mathf.Min(1f, v + 0.30f));
    }

    // ── Bar construction ──────────────────────────────────────────────────────

    private void BuildBar()
    {
        Sprite px = GetPixelSprite();

        _root = new GameObject("_HealthBar").transform;
        _root.SetParent(transform);
        _root.localPosition = Vector3.zero;
        _root.localRotation = Quaternion.identity;
        _root.localScale    = Vector3.one;

        // Background
        var bgGO = new GameObject("BG");
        bgGO.transform.SetParent(_root);
        bgGO.transform.localPosition = Vector3.zero;
        bgGO.transform.localRotation = Quaternion.identity;
        bgGO.transform.localScale    = new Vector3(BarWidth + BgPadX * 2f, BarHeight + BgPadY * 2f, 1f);
        _bgSR                = bgGO.AddComponent<SpriteRenderer>();
        _bgSR.sprite         = px;
        _bgSR.color          = BgCol;
        _bgSR.sortingLayerID = _sourceSR.sortingLayerID;
        _bgSR.sortingOrder   = _sourceSR.sortingOrder + 3;

        // Segments (left to right = lowest to highest HP threshold)
        float segW = (BarWidth - (SegCount - 1) * SegGap) / SegCount;
        for (int i = 0; i < SegCount; i++)
        {
            float xCenter = -BarWidth * 0.5f + segW * 0.5f + i * (segW + SegGap);

            var go = new GameObject($"Seg{i}");
            go.transform.SetParent(_root);
            go.transform.localPosition = new Vector3(xCenter, 0f, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = new Vector3(segW, BarHeight, 1f);

            var sr           = go.AddComponent<SpriteRenderer>();
            sr.sprite        = px;
            sr.color         = EmptyCol;
            sr.sortingLayerID = _sourceSR.sortingLayerID;
            sr.sortingOrder  = _sourceSR.sortingOrder + 4;

            _segSRs[i] = sr;
        }
    }

    // ── Shared 1×1 white pixel sprite ─────────────────────────────────────────

    private static Sprite GetPixelSprite()
    {
        if (_pixSprite != null) return _pixSprite;
        _pixTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp,
        };
        _pixTex.SetPixel(0, 0, Color.white);
        _pixTex.Apply();
        _pixSprite = Sprite.Create(_pixTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return _pixSprite;
    }
}
