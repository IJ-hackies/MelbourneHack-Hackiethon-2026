using UnityEngine;

/// <summary>
/// Procedural world-space health bar for enemies.
/// Auto-attached by EnemyBase.Awake() — do NOT manually add this component.
///
/// Bar theming is configured via the "Bar Color" field on the EnemyBase component.
/// Leave it at (0,0,0,0) to use the default green→yellow→red health gradient.
/// Set it to a tinted colour (with alpha > 0) for elemental / special enemies.
///
/// The fill shrinks right-to-left with HP. Custom-colored bars brighten at full
/// HP and darken toward a muted shade as health drops.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Health))]
public class EnemyHealthBar : MonoBehaviour
{
    // ── Layout (world units) ──────────────────────────────────────────────────
    private const float BarWidth    = 0.80f;
    private const float BarHeight   = 0.12f;
    private const float BgPadX      = 0.03f;
    private const float BgPadY      = 0.025f;
    private const float HeadOffset  = 0.12f;

    // ── Timing ────────────────────────────────────────────────────────────────
    private const float ShowDuration   = 2.0f;
    private const float FadeOutTime    = 0.5f;
    private const float FlashDuration  = 0.28f;
    private const float BounceDuration = 0.25f;
    private const float BounceAmount   = 0.12f;

    // ── Default health-gradient palette (brighter than before) ───────────────
    private static readonly Color HealthHigh   = new Color(0.30f, 1.00f, 0.30f, 1f); // vivid green
    private static readonly Color HealthMid    = new Color(1.00f, 0.95f, 0.20f, 1f); // vivid yellow
    private static readonly Color HealthLow    = new Color(1.00f, 0.25f, 0.25f, 1f); // vivid red
    private static readonly Color BgCol        = new Color(0.28f, 0.28f, 0.28f, 1f);
    private static readonly Color FlashDmgCol  = new Color(1.00f, 0.12f, 0.12f, 1f);
    private static readonly Color FlashHealCol = new Color(0.20f, 1.00f, 0.20f, 1f);

    // ── Runtime refs ──────────────────────────────────────────────────────────
    private Health         _health;
    private SpriteRenderer _sourceSR;
    private Color          _barColor;

    private Transform      _root;
    private SpriteRenderer _bgSR;
    private SpriteRenderer _fillSR;   // single continuous fill, shrinks right-to-left

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

        float curr = _health.Current;
        if (curr < _prevHp - 0.01f)
            Activate(DamageFlash());
        else if (curr > _prevHp + 0.01f)
            Activate(HealFlash());
        _prevHp = curr;

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
        float worldTop = _sourceSR.sprite != null
            ? _sourceSR.bounds.max.y
            : transform.position.y + 0.5f;

        _root.position = new Vector3(
            transform.position.x,
            worldTop + HeadOffset,
            transform.position.z - 0.01f);

        Vector3 ls = transform.lossyScale;
        float sx = ls.x != 0f ? 1f / ls.x : 1f;
        float sy = ls.y != 0f ? 1f / ls.y : 1f;

        float bounce = 0f;
        if (_bounceTimer > 0f)
        {
            float t = 1f - _bounceTimer / BounceDuration;
            bounce = BounceAmount * Mathf.Sin(t * Mathf.PI);
        }
        float boost = 1f + bounce;
        _root.localScale = new Vector3(sx * boost, sy * boost, 1f);

        int baseOrder = _sourceSR.sortingOrder + 3;
        _bgSR.sortingOrder   = baseOrder;
        _fillSR.sortingOrder = baseOrder + 1;
    }

    // ── Colours ───────────────────────────────────────────────────────────────

    private void ApplyColors()
    {
        float ratio  = _health.Max > 0f ? Mathf.Clamp01(_health.Current / _health.Max) : 0f;
        float alpha  = _fadeAlpha;
        float flashT = (_flashTimer / FlashDuration) * 0.25f;

        // ── Fill: shrink right-to-left ────────────────────────────────────────
        // Left edge stays fixed at -BarWidth/2; right edge tracks ratio
        float fillW = Mathf.Max(ratio * BarWidth, 0.001f);
        float fillX = -BarWidth * 0.5f + fillW * 0.5f;
        _fillSR.transform.localPosition = new Vector3(fillX, 0f, 0f);
        _fillSR.transform.localScale    = new Vector3(fillW, BarHeight, 1f);

        Color fillBase = FillColor(ratio);
        Color fill     = Color.Lerp(fillBase, _flashColor, flashT);
        fill.a = alpha;
        _fillSR.color = fill;

        Color bg = Color.Lerp(BgCol, _flashColor, flashT * 0.5f);
        bg.a = alpha;
        _bgSR.color = bg;
    }

    private void SetFadeAlpha(float a)
    {
        _fadeAlpha = a;
        bool on = a > 0.01f;
        if (_bgSR   != null) _bgSR.enabled   = on;
        if (_fillSR != null) _fillSR.enabled  = on;
    }

    // ── Theme helpers ─────────────────────────────────────────────────────────

    private bool UseCustomColor => _barColor.a > 0.01f;

    /// Returns the fill color at a given HP ratio (0=empty, 1=full).
    private Color FillColor(float ratio)
    {
        if (!UseCustomColor)
        {
            // Default: vivid green → yellow → red as HP drops
            if (ratio > 0.5f)
                return Color.Lerp(HealthMid, HealthHigh, (ratio - 0.5f) * 2f);
            else
                return Color.Lerp(HealthLow, HealthMid, ratio * 2f);
        }

        // Custom bar color: bright and vivid at full HP, darker/more saturated at low HP
        Color.RGBToHSV(_barColor, out float h, out float s, out float v);
        float targetV = Mathf.Lerp(0.80f, 1.00f, ratio);        // darken as HP drops (floor 0.80 keeps it bright)
        float targetS = Mathf.Lerp(Mathf.Min(1f, s + 0.15f), s, ratio);
        return Color.HSVToRGB(h, Mathf.Clamp01(targetS), Mathf.Clamp01(targetV));
    }

    private Color DamageFlash()
    {
        if (!UseCustomColor) return FlashDmgCol;
        Color.RGBToHSV(_barColor, out float h, out float s, out float v);
        return Color.HSVToRGB(h, Mathf.Min(1f, s + 0.20f), Mathf.Max(0.1f, v - 0.30f));
    }

    private Color HealFlash()
    {
        if (!UseCustomColor) return FlashHealCol;
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

        // Background — full width, slightly padded
        var bgGO = new GameObject("BG");
        bgGO.transform.SetParent(_root);
        bgGO.transform.localPosition = Vector3.zero;
        bgGO.transform.localRotation = Quaternion.identity;
        bgGO.transform.localScale    = new Vector3(BarWidth + BgPadX * 2f, BarHeight + BgPadY * 2f, 1f);
        _bgSR               = bgGO.AddComponent<SpriteRenderer>();
        _bgSR.sprite        = px;
        _bgSR.color         = BgCol;
        _bgSR.sortingLayerID = _sourceSR.sortingLayerID;
        _bgSR.sortingOrder  = _sourceSR.sortingOrder + 3;

        // Fill — starts full-width, shrinks in ApplyColors each frame
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(_root);
        fillGO.transform.localPosition = Vector3.zero;
        fillGO.transform.localRotation = Quaternion.identity;
        fillGO.transform.localScale    = new Vector3(BarWidth, BarHeight, 1f);
        _fillSR               = fillGO.AddComponent<SpriteRenderer>();
        _fillSR.sprite        = px;
        _fillSR.color         = HealthHigh;
        _fillSR.sortingLayerID = _sourceSR.sortingLayerID;
        _fillSR.sortingOrder  = _sourceSR.sortingOrder + 4;
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
