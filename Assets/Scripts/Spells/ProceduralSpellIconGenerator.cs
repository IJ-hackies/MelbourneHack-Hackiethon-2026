using UnityEngine;

/// <summary>
/// Generates a 64×64 pixel-art spell icon instantly using Texture2D pixel math.
/// No API calls — transparent background, colored from the spell's projectileColor/secondaryColor.
///
/// Variation sources:
///   • Primary movement tag  → base shape (orb / ring / star / oval)
///   • Sub-shape variants    → burstCount arms, glowSize halo, speed elongation, doubleRing
///   • Name hash             → pseudo-random rotation/offset per spell so identical configs still differ
///   • Element               → centered symbol, rotated by name hash
///   • Behavior modifier tags → up to 2 small corner overlays (arrow, burst, chain, pierce, etc.)
///   • Corruption tags       → red tint + crack lines
/// </summary>
public static class ProceduralSpellIconGenerator
{
    private const int   Size = 64;
    private const float Cx   = Size / 2f - 0.5f;
    private const float Cy   = Size / 2f - 0.5f;

    // Corner positions for behavior overlays (pixel-center of each ~12×12 corner zone)
    private static readonly (float x, float y)[] Corners =
    {
        (11f, 52f),  // top-left
        (52f, 52f),  // top-right
        (11f, 11f),  // bottom-left
        (52f, 11f),  // bottom-right
    };

    // ── Public API ──────────────────────────────────────────────────────────────

    public static Sprite Generate(SpellData spell)
    {
        Color primary   = ParseColor(spell.projectileColor, new Color(0.55f, 0.35f, 1f));
        Color secondary = ParseColor(spell.secondaryColor,  Color.Lerp(primary, Color.white, 0.45f));
        int   seed      = GetNameHash(spell.spellName);

        Color[] pixels = new Color[Size * Size];

        // ── Base shape ──────────────────────────────────────────────────────────
        if (spell.HasTag(SpellTag.ORBITAL))
        {
            bool doubleRing = (seed & 1) == 0
                           || spell.HasTag(SpellTag.ECHOING)
                           || spell.HasTag(SpellTag.MIRRORED);
            DrawRing(pixels, primary, secondary, doubleRing);
        }
        else if (spell.HasTag(SpellTag.BEAM))
        {
            // burstCount drives arm count; seed adds ±1 micro-variation
            int arms = spell.burstCount > 1
                ? Mathf.Clamp(spell.burstCount * 2 + 2, 4, 14)
                : 8;
            arms = Mathf.Clamp(arms + (seed % 3) - 1, 4, 14);
            DrawStar(pixels, primary, secondary, arms, seed);
        }
        else if (spell.HasTag(SpellTag.CHANNELED))
        {
            // speed stretches the oval; seed varies tilt in 22.5° steps
            float elongation = spell.speed > 0f ? Mathf.Clamp(spell.speed / 10f, 0.6f, 2.2f) : 1f;
            float tiltAngle  = Mathf.PI / 4f + (seed % 4) * (Mathf.PI / 8f);
            DrawOval(pixels, primary, secondary, elongation, tiltAngle);
        }
        else // PROJECTILE / default
        {
            // glowSize scales halo; certain tags / seed add inner ring detail
            float haloScale = spell.glowSize > 0f ? Mathf.Clamp(spell.glowSize, 0.5f, 1.8f) : 1f;
            bool  innerRing = spell.HasTag(SpellTag.PERSISTENT)
                           || spell.HasTag(SpellTag.DELAYED)
                           || spell.HasTag(SpellTag.PHASING)
                           || (seed % 3 == 0);
            DrawOrb(pixels, primary, secondary, haloScale, innerRing);
        }

        // ── Element symbol (rotated by name hash) ───────────────────────────────
        float symRot = (seed % 8) * 45f * Mathf.Deg2Rad;
        DrawElementSymbol(pixels, spell.element, primary, symRot);

        // ── Behavior-tag corner overlays (up to 2) ──────────────────────────────
        DrawBehaviorOverlays(pixels, spell, secondary, seed);

        // ── Corruption tint + cracks ─────────────────────────────────────────────
        if (spell.HasTag(SpellTag.SELF_DAMAGE)
         || spell.HasTag(SpellTag.ENEMY_HOMING)
         || spell.HasTag(SpellTag.REVERSED_CONTROLS))
            ApplyCorruptionTint(pixels, seed);

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode   = TextureWrapMode.Clamp;
        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
    }

    // ── Base shapes ─────────────────────────────────────────────────────────────

    private static void DrawOrb(Color[] p, Color primary, Color secondary, float haloScale, bool innerRing)
    {
        float outer = Size * 0.40f;
        float halo  = Mathf.Max(Size * 0.50f * haloScale, outer + 2f);

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float d = Dist(x, y);
            if (d <= outer)
            {
                float t = d / outer;
                Color c = Color.Lerp(Color.Lerp(Color.white, primary, t * 0.7f), secondary, t * t);
                c.a = 1f;
                p[Idx(x, y)] = c;
            }
            else if (d <= halo)
            {
                float t = (d - outer) / (halo - outer);
                Color c = secondary;
                c.a = Mathf.Lerp(0.5f, 0f, t * t);
                p[Idx(x, y)] = BlendOver(p[Idx(x, y)], c);
            }
        }

        if (innerRing)
        {
            float ringR = outer * 0.55f;
            const float ringW = 2.5f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = Dist(x, y);
                if (Mathf.Abs(d - ringR) <= ringW)
                {
                    float t = 1f - Mathf.Abs(d - ringR) / ringW;
                    Color c = Color.Lerp(primary, Color.white, 0.5f);
                    c.a = t * 0.6f;
                    p[Idx(x, y)] = BlendOver(p[Idx(x, y)], c);
                }
            }
        }
    }

    private static void DrawRing(Color[] p, Color primary, Color secondary, bool doubleRing)
    {
        DrawRingBand(p, primary, secondary, Size * 0.42f, Size * 0.24f, blend: false);
        if (doubleRing)
            DrawRingBand(p, secondary, primary, Size * 0.18f, Size * 0.07f, blend: true);

        // Center dot
        float dotR = Size * 0.10f;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float d = Dist(x, y);
            if (d <= dotR)
            {
                Color c = Color.Lerp(Color.white, primary, d / dotR);
                c.a = 1f;
                p[Idx(x, y)] = BlendOver(p[Idx(x, y)], c);
            }
        }
    }

    private static void DrawRingBand(Color[] p, Color primary, Color secondary,
                                     float rOuter, float rInner, bool blend)
    {
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float d = Dist(x, y);
            if (d < rInner || d > rOuter) continue;

            float angle = Mathf.Atan2(y - Cy, x - Cx);
            float t = (angle + Mathf.PI) / (2f * Mathf.PI);
            Color c = Color.Lerp(primary, secondary, t);
            c.a = Mathf.Min(
                Mathf.InverseLerp(rInner, rInner + 3f, d),
                Mathf.InverseLerp(rOuter, rOuter - 3f, d));

            p[Idx(x, y)] = blend ? BlendOver(p[Idx(x, y)], c) : c;
        }
    }

    private static void DrawStar(Color[] p, Color primary, Color secondary, int arms, int seed)
    {
        float outer = Size * 0.43f;
        float inner = Size * 0.18f;
        // Rotate star so same arm-count looks different per-spell
        float rotOffset = (seed % arms) * (Mathf.PI * 2f / arms);

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float d     = Dist(x, y);
            float angle = Mathf.Atan2(y - Cy, x - Cx) + rotOffset;
            float armT  = Mathf.Abs(Mathf.Cos(angle * arms / 2f));
            float limit = Mathf.Lerp(inner, outer, armT);
            if (d > limit) continue;

            float t = d / limit;
            Color c = Color.Lerp(Color.Lerp(Color.white, primary, t * 0.6f), secondary, t);
            c.a = Mathf.Lerp(1f, 0f, Mathf.Max(0f, (t - 0.8f) / 0.2f));
            p[Idx(x, y)] = c;
        }
    }

    private static void DrawOval(Color[] p, Color primary, Color secondary, float elongation, float tiltAngle)
    {
        float a = Size * 0.44f * Mathf.Sqrt(elongation);
        float b = Mathf.Max(Size * 0.22f / Mathf.Sqrt(elongation), Size * 0.12f);
        float cosA = Mathf.Cos(tiltAngle), sinA = Mathf.Sin(tiltAngle);

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float dx = x - Cx, dy = y - Cy;
            float rx = dx * cosA + dy * sinA;
            float ry = -dx * sinA + dy * cosA;
            float oval = (rx * rx) / (a * a) + (ry * ry) / (b * b);
            if (oval > 1f) continue;

            float t = Mathf.Sqrt(oval);
            Color c = Color.Lerp(Color.Lerp(Color.white, primary, t * 0.7f), secondary, t * t);
            c.a = 1f;
            p[Idx(x, y)] = c;
        }
    }

    // ── Element symbols ──────────────────────────────────────────────────────────

    private static void DrawElementSymbol(Color[] p, string element, Color primary, float rotation)
    {
        Color sym = Color.Lerp(primary, Color.black, 0.55f);
        sym.a = 0.9f;

        switch ((element ?? "").ToLowerInvariant())
        {
            case "fire":
                DrawTriangle(p, sym, Cx, Cy,      11f, up: true,  rotation: rotation);
                DrawTriangle(p, sym, Cx, Cy + 4f,  7f, up: true,  rotation: rotation);
                break;

            case "ice":
            case "frozen":
                DrawCross(p, sym, Cx, Cy, 10f, 2f, rotation);
                DrawCross(p, sym, Cx, Cy, 10f, 2f, rotation + Mathf.PI / 4f);
                break;

            case "shadow":
            case "dark":
                DrawCrescent(p, sym, Cx, Cy, 10f, rotation);
                break;

            case "poison":
            case "nature":
                DrawDrop(p, sym, Cx, Cy, 10f, rotation);
                break;

            case "lightning":
            case "electric":
                DrawLightning(p, sym, Cx, Cy, 10f, rotation);
                break;

            default: // arcane / void / untyped — rotatable cross
                DrawCross(p, sym, Cx, Cy, 9f, 2f, rotation);
                break;
        }
    }

    // ── Behavior-tag overlays ────────────────────────────────────────────────────

    private static void DrawBehaviorOverlays(Color[] pixels, SpellData spell, Color secondary, int seed)
    {
        Color ovCol = Color.Lerp(secondary, Color.white, 0.55f);
        ovCol.a = 0.85f;

        // Priority order — first two matching tags claim corners
        SpellTag[] priority =
        {
            SpellTag.HOMING,       SpellTag.SENTIENT,
            SpellTag.AOE_BURST,    SpellTag.DETONATING,   SpellTag.FRAGMENTING,
            SpellTag.CHAIN,
            SpellTag.PIERCE,       SpellTag.PIERCE_WALLS,
            SpellTag.BOOMERANG,
            SpellTag.BURN,         SpellTag.LINGERING,
            SpellTag.FREEZE,       SpellTag.SLOW,
            SpellTag.LIFESTEAL,
            SpellTag.STUN,
            SpellTag.WALL_BOUNCE,  SpellTag.REFLECTING,
            SpellTag.SACRIFICE,
            SpellTag.SPIRAL,
            SpellTag.SPLIT_ON_IMPACT,
            SpellTag.BLEED,
            SpellTag.POISON,
        };

        int drawn      = 0;
        int cornerBase = seed & 3;

        foreach (SpellTag tag in priority)
        {
            if (drawn >= 2) break;
            if (!spell.HasTag(tag)) continue;
            var (cx, cy) = Corners[(cornerBase + drawn) & 3];
            DrawTagOverlay(pixels, tag, ovCol, cx, cy);
            drawn++;
        }
    }

    private static void DrawTagOverlay(Color[] p, SpellTag tag, Color c, float cx, float cy)
    {
        const float S = 9f;
        switch (tag)
        {
            case SpellTag.HOMING:
            case SpellTag.SENTIENT:
                DrawOverlayArrow(p, c, cx, cy, S);
                break;

            case SpellTag.AOE_BURST:
            case SpellTag.DETONATING:
            case SpellTag.FRAGMENTING:
                DrawOverlayBurst(p, c, cx, cy, S);
                break;

            case SpellTag.CHAIN:
                DrawOverlayDots(p, c, cx, cy, S);
                break;

            case SpellTag.PIERCE:
            case SpellTag.PIERCE_WALLS:
                DrawOverlayLines(p, c, cx, cy, S);
                break;

            case SpellTag.BOOMERANG:
            case SpellTag.SPIRAL:
                DrawOverlayArc(p, c, cx, cy, S);
                break;

            case SpellTag.BURN:
            case SpellTag.LINGERING:
                DrawOverlayFlame(p, c, cx, cy, S);
                break;

            case SpellTag.FREEZE:
            case SpellTag.SLOW:
                DrawOverlaySnowflake(p, c, cx, cy, S);
                break;

            case SpellTag.LIFESTEAL:
                DrawOverlayHeart(p, c, cx, cy, S);
                break;

            case SpellTag.STUN:
                DrawOverlayStun(p, c, cx, cy, S);
                break;

            case SpellTag.WALL_BOUNCE:
            case SpellTag.REFLECTING:
                DrawOverlayBounce(p, c, cx, cy, S);
                break;

            case SpellTag.SACRIFICE:
                DrawOverlayX(p, c, cx, cy, S);
                break;

            case SpellTag.SPLIT_ON_IMPACT:
                DrawOverlaySplit(p, c, cx, cy, S);
                break;

            case SpellTag.BLEED:
            case SpellTag.POISON:
                DrawOverlayDrip(p, c, cx, cy, S);
                break;

            default:
                FillCircle(p, c, cx, cy, 3f, 1f);
                break;
        }
    }

    // ── Overlay symbol primitives ────────────────────────────────────────────────

    // ∧ chevron = homing
    private static void DrawOverlayArrow(Color[] p, Color c, float cx, float cy, float s)
    {
        float h = s * 0.45f;
        DrawLine(p, c, cx - h, cy - h * 0.5f, cx,      cy + h * 0.5f, 1.5f);
        DrawLine(p, c, cx + h, cy - h * 0.5f, cx,      cy + h * 0.5f, 1.5f);
    }

    // 4-ray starburst = explosion / AOE
    private static void DrawOverlayBurst(Color[] p, Color c, float cx, float cy, float s)
    {
        float r = s * 0.48f;
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI / 2f + Mathf.PI / 4f;
            DrawLine(p, c, cx, cy, cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a), 1.5f);
        }
        FillCircle(p, c, cx, cy, 2f, 1f);
    }

    // Three diagonal dots = chain
    private static void DrawOverlayDots(Color[] p, Color c, float cx, float cy, float s)
    {
        float step = s * 0.32f;
        FillCircle(p, c, cx - step, cy - step, 1.8f, 1f);
        FillCircle(p, c, cx,        cy,        1.8f, 1f);
        FillCircle(p, c, cx + step, cy + step, 1.8f, 1f);
    }

    // Three horizontal lines = pierce
    private static void DrawOverlayLines(Color[] p, Color c, float cx, float cy, float s)
    {
        float r       = s * 0.45f;
        float spacing = s * 0.28f;
        for (int i = -1; i <= 1; i++)
            DrawLine(p, c, cx - r, cy + i * spacing, cx + r, cy + i * spacing, 1.2f);
    }

    // Half-circle arc = boomerang / spiral
    private static void DrawOverlayArc(Color[] p, Color c, float cx, float cy, float s)
    {
        float r = s * 0.42f;
        const int segments = 6;
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.PI + i       * Mathf.PI / segments;
            float a1 = Mathf.PI + (i + 1) * Mathf.PI / segments;
            DrawLine(p, c,
                cx + r * Mathf.Cos(a0), cy + r * Mathf.Sin(a0),
                cx + r * Mathf.Cos(a1), cy + r * Mathf.Sin(a1), 1.2f);
        }
    }

    // Upward triangle = flame / burn
    private static void DrawOverlayFlame(Color[] p, Color c, float cx, float cy, float s)
        => DrawTriangle(p, c, cx, cy, s * 0.9f, up: true, rotation: 0f);

    // 3-axis asterisk = snowflake / freeze
    private static void DrawOverlaySnowflake(Color[] p, Color c, float cx, float cy, float s)
    {
        float r = s * 0.45f;
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.PI / 3f;
            DrawLine(p, c,
                cx - r * Mathf.Cos(a), cy - r * Mathf.Sin(a),
                cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a), 1.2f);
        }
        FillCircle(p, c, cx, cy, 1.5f, 1f);
    }

    // Two circles + downward triangle = heart
    private static void DrawOverlayHeart(Color[] p, Color c, float cx, float cy, float s)
    {
        float r = s * 0.22f;
        FillCircle(p, c, cx - r, cy + r * 0.3f, r, 1f);
        FillCircle(p, c, cx + r, cy + r * 0.3f, r, 1f);
        DrawTriangle(p, c, cx, cy - r * 0.5f, s * 0.5f, up: false, rotation: 0f);
    }

    // Three dots in triangle arrangement = stun
    private static void DrawOverlayStun(Color[] p, Color c, float cx, float cy, float s)
    {
        float r = s * 0.35f;
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2f * Mathf.PI / 3f - Mathf.PI / 2f;
            FillCircle(p, c, cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a), 1.8f, 1f);
        }
    }

    // > bracket = bounce
    private static void DrawOverlayBounce(Color[] p, Color c, float cx, float cy, float s)
    {
        float h = s * 0.45f;
        DrawLine(p, c, cx - h * 0.5f, cy + h, cx + h * 0.5f, cy, 1.5f);
        DrawLine(p, c, cx - h * 0.5f, cy - h, cx + h * 0.5f, cy, 1.5f);
    }

    // × = sacrifice
    private static void DrawOverlayX(Color[] p, Color c, float cx, float cy, float s)
    {
        float h = s * 0.42f;
        DrawLine(p, c, cx - h, cy - h, cx + h, cy + h, 1.5f);
        DrawLine(p, c, cx - h, cy + h, cx + h, cy - h, 1.5f);
    }

    // Y shape = split
    private static void DrawOverlaySplit(Color[] p, Color c, float cx, float cy, float s)
    {
        float h = s * 0.42f;
        DrawLine(p, c, cx, cy,       cx,      cy + h,      1.5f); // stem up
        DrawLine(p, c, cx, cy - h * 0.2f, cx - h, cy - h, 1.5f); // left fork
        DrawLine(p, c, cx, cy - h * 0.2f, cx + h, cy - h, 1.5f); // right fork
    }

    // Drip / teardrop = bleed / poison
    private static void DrawOverlayDrip(Color[] p, Color c, float cx, float cy, float s)
    {
        float r = s * 0.28f;
        FillCircle(p, c, cx, cy - r * 0.5f, r, 1f);
        DrawLine(p, c, cx, cy - r * 0.5f + r, cx, cy + s * 0.4f, 1.5f);
    }

    // ── Corruption tint ──────────────────────────────────────────────────────────

    private static void ApplyCorruptionTint(Color[] pixels, int seed)
    {
        Color tint = new Color(0.75f, 0.05f, 0.15f, 0.4f);
        for (int i = 0; i < pixels.Length; i++)
            if (pixels[i].a > 0.01f)
                pixels[i] = BlendOver(pixels[i], tint);

        // Two diagonal cracks
        Color crackCol   = new Color(1f, 0.1f, 0.1f, 0.75f);
        float crackAngle = (seed % 6) * 30f * Mathf.Deg2Rad;
        DrawCrackLine(pixels, crackCol, crackAngle,                  Size * 0.38f);
        DrawCrackLine(pixels, crackCol, crackAngle + Mathf.PI * 0.4f + (seed % 3) * 0.3f, Size * 0.26f);
    }

    private static void DrawCrackLine(Color[] p, Color c, float angle, float len)
    {
        float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        DrawLine(p, c,
            Cx - cos * len * 0.5f, Cy - sin * len * 0.5f,
            Cx + cos * len * 0.5f, Cy + sin * len * 0.5f, 1f);
    }

    // ── Symbol primitives ────────────────────────────────────────────────────────

    private static void DrawTriangle(Color[] p, Color c, float cx, float cy, float size, bool up, float rotation)
    {
        float h    = size * 0.866f;
        float cosR = Mathf.Cos(rotation), sinR = Mathf.Sin(rotation);

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float dx = x - cx, dy = y - cy;
            float rx = dx * cosR + dy * sinR;
            float ry = -dx * sinR + dy * cosR;
            float ny = up ? -ry : ry;
            if (ny < -h * 0.5f || ny > h * 0.5f) continue;
            float rowW = size * (ny + h * 0.5f) / h;
            if (Mathf.Abs(rx) <= rowW * 0.5f)
                p[Idx(x, y)] = BlendOver(p[Idx(x, y)], c);
        }
    }

    private static void DrawCross(Color[] p, Color c, float cx, float cy,
                                  float radius, float thickness, float angle = 0f)
    {
        float halfT = thickness * 0.5f;
        float cosR  = Mathf.Cos(angle), sinR = Mathf.Sin(angle);

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float dx = x - cx, dy = y - cy;
            float rx = dx * cosR + dy * sinR;
            float ry = -dx * sinR + dy * cosR;
            bool onH = Mathf.Abs(ry) <= halfT && Mathf.Abs(rx) <= radius;
            bool onV = Mathf.Abs(rx) <= halfT && Mathf.Abs(ry) <= radius;
            if (onH || onV)
                p[Idx(x, y)] = BlendOver(p[Idx(x, y)], c);
        }
    }

    private static void DrawCrescent(Color[] p, Color c, float cx, float cy, float r, float offsetAngle)
    {
        float innerR = r * 0.7f;
        float offX   = r * 0.35f * Mathf.Cos(offsetAngle);
        float offY   = r * 0.35f * Mathf.Sin(offsetAngle);
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float outerD = Dist(x, y, cx,        cy);
            float innerD = Dist(x, y, cx + offX, cy + offY);
            if (outerD <= r && innerD > innerR)
                p[Idx(x, y)] = BlendOver(p[Idx(x, y)], c);
        }
    }

    private static void DrawDrop(Color[] p, Color c, float cx, float cy, float size, float rotation)
    {
        float r    = size * 0.55f;
        float cosR = Mathf.Cos(rotation), sinR = Mathf.Sin(rotation);
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float dx = x - cx, dy = y - cy;
            float rx = dx * cosR + dy * sinR;
            float ry = -dx * sinR + dy * cosR;
            if (Mathf.Sqrt(rx * rx + (ry - size * 0.1f) * (ry - size * 0.1f)) <= r)
            { p[Idx(x, y)] = BlendOver(p[Idx(x, y)], c); continue; }
            float ny = -(ry + size * 0.3f);
            if (ny >= 0f && ny <= size * 0.55f && Mathf.Abs(rx) <= ny * 0.5f)
                p[Idx(x, y)] = BlendOver(p[Idx(x, y)], c);
        }
    }

    private static void DrawLightning(Color[] p, Color c, float cx, float cy, float size, float rotation)
    {
        float s    = size * 0.5f;
        float cosR = Mathf.Cos(rotation), sinR = Mathf.Sin(rotation);
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float dx = x - cx, dy = y - cy;
            float rx = (dx * cosR + dy * sinR) / s;
            float ry = (-dx * sinR + dy * cosR) / s;
            float dTop = SegmentDist(rx, ry, -0.15f, -1f,  0.35f,  0f);
            float dBot = SegmentDist(rx, ry, -0.35f,  0f,  0.15f,  1f);
            if ((dTop < 0.18f || dBot < 0.18f) && Mathf.Abs(ry) <= 1f)
                p[Idx(x, y)] = BlendOver(p[Idx(x, y)], c);
        }
    }

    // ── Rasteriser helpers ───────────────────────────────────────────────────────

    private static void FillCircle(Color[] p, Color c, float cx, float cy, float r, float alpha)
    {
        c.a = alpha;
        int x0 = Mathf.Max(0,        Mathf.FloorToInt(cx - r - 1));
        int x1 = Mathf.Min(Size - 1, Mathf.CeilToInt (cx + r + 1));
        int y0 = Mathf.Max(0,        Mathf.FloorToInt(cy - r - 1));
        int y1 = Mathf.Min(Size - 1, Mathf.CeilToInt (cy + r + 1));
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float d = Dist(x, y, cx, cy);
            if (d <= r)
            {
                Color oc = c;
                oc.a = alpha * Mathf.InverseLerp(r, r - 1f, d);
                p[Idx(x, y)] = BlendOver(p[Idx(x, y)], oc);
            }
        }
    }

    private static void DrawLine(Color[] p, Color c,
                                 float x0, float y0, float x1, float y1, float halfWidth)
    {
        int minX = Mathf.Max(0,        Mathf.FloorToInt(Mathf.Min(x0, x1) - halfWidth - 1));
        int maxX = Mathf.Min(Size - 1, Mathf.CeilToInt (Mathf.Max(x0, x1) + halfWidth + 1));
        int minY = Mathf.Max(0,        Mathf.FloorToInt(Mathf.Min(y0, y1) - halfWidth - 1));
        int maxY = Mathf.Min(Size - 1, Mathf.CeilToInt (Mathf.Max(y0, y1) + halfWidth + 1));

        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            float d = SegmentDist(x, y, x0, y0, x1, y1);
            if (d < halfWidth + 1f)
            {
                float a = c.a * Mathf.Clamp01(1f - (d - halfWidth));
                if (a > 0f)
                {
                    Color oc = c; oc.a = a;
                    p[Idx(x, y)] = BlendOver(p[Idx(x, y)], oc);
                }
            }
        }
    }

    // ── Math helpers ─────────────────────────────────────────────────────────────

    private static float Dist(float x, float y) =>
        Mathf.Sqrt((x - Cx) * (x - Cx) + (y - Cy) * (y - Cy));

    private static float Dist(int x, int y, float cx, float cy) =>
        Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

    private static float Dist(float x, float y, float cx, float cy) =>
        Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

    private static int Idx(int x, int y) => y * Size + x;

    private static Color BlendOver(Color dst, Color src)
    {
        float a = src.a + dst.a * (1f - src.a);
        if (a <= 0f) return Color.clear;
        return new Color(
            (src.r * src.a + dst.r * dst.a * (1f - src.a)) / a,
            (src.g * src.a + dst.g * dst.a * (1f - src.a)) / a,
            (src.b * src.a + dst.b * dst.a * (1f - src.a)) / a,
            a);
    }

    private static float SegmentDist(float px, float py,
                                     float ax, float ay, float bx, float by)
    {
        float abx = bx - ax, aby = by - ay;
        float t = Mathf.Clamp01(((px - ax) * abx + (py - ay) * aby) /
                                 (abx * abx + aby * aby + 1e-6f));
        float dx = px - (ax + t * abx), dy = py - (ay + t * aby);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private static Color ParseColor(string hex, Color fallback) =>
        !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;

    /// Stable hash of spell name → int seed for pseudo-random variation.
    private static int GetNameHash(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        int h = 17;
        foreach (char ch in name) h = h * 31 + ch;
        return Mathf.Abs(h);
    }
}
