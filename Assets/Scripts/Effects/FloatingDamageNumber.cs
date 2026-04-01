using UnityEngine;
using TMPro;

/// <summary>
/// World-space floating damage/heal number using a tiny canvas so TMP renders
/// correctly in URP 2D. Drifts up (heal) or down (damage) with random X scatter,
/// scale-punches on spawn, fades out, and self-destructs.
/// </summary>
public class FloatingDamageNumber : MonoBehaviour
{
    private TextMeshProUGUI tmp;
    private CanvasGroup canvasGroup;
    private Color baseColor;
    private float elapsed;
    private Vector3 drift;
    private float startScaleMultiplier;

    private const float Duration = 0.6f;
    private const float DriftSpeed = 1.2f;
    private const float ScalePunch = 1.3f;
    private const float ScalePunchDuration = 0.1f;
    private const float XScatter = 0.4f;
    private const float WorldScale = 0.012f; // Canvas-to-world scale

    // Crit overrides
    private const float CritScalePunch = 1.8f;
    private const float CritScalePunchDuration = 0.15f;
    private const float CritDuration = 0.8f;
    private const float CritWorldScale = 0.016f;

    private static readonly Color DamageColor = new Color(1f, 0.2f, 0.2f, 1f);
    private static readonly Color32 DamageOutline = new Color32(40, 0, 0, 255);
    private static readonly Color CritColor = new Color(1f, 0.85f, 0.1f, 1f);
    private static readonly Color32 CritOutline = new Color32(60, 30, 0, 255);
    private static readonly Color HealColor = new Color(0.2f, 1f, 0.35f, 1f);
    private static readonly Color32 HealOutline = new Color32(0, 40, 8, 255);

    public static FloatingDamageNumber Spawn(Vector3 worldPos, float amount, bool isHeal,
                                              TMP_FontAsset font, bool isCrit = false)
    {
        // Root object
        var go = new GameObject(isCrit ? "DmgNumCrit" : "DmgNum");
        go.transform.position = worldPos + new Vector3(Random.Range(-XScatter, XScatter), 0.5f, 0f);

        var fdn = go.AddComponent<FloatingDamageNumber>();
        fdn.Build(amount, isHeal, font, isCrit);
        return fdn;
    }

    private float duration;
    private float scalePunch;
    private float scalePunchDur;

    private void Build(float amount, bool isHeal, TMP_FontAsset font, bool isCrit = false)
    {
        // Pick timing based on crit
        float ws = (isCrit && !isHeal) ? CritWorldScale : WorldScale;
        scalePunch = (isCrit && !isHeal) ? CritScalePunch : ScalePunch;
        scalePunchDur = (isCrit && !isHeal) ? CritScalePunchDuration : ScalePunchDuration;
        duration = (isCrit && !isHeal) ? CritDuration : Duration;

        // World-space canvas
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingLayerName = "Foreground";
        canvas.sortingOrder = 100;

        var rt = GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200f, 80f);

        canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Scale canvas down to world units
        transform.localScale = Vector3.one * ws * scalePunch;
        startScaleMultiplier = ws;

        // Text
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(transform, false);

        tmp = textGO.AddComponent<TextMeshProUGUI>();
        var textRT = tmp.rectTransform;
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        tmp.text = isHeal ? $"+{Mathf.RoundToInt(amount)}"
                 : isCrit ? $"-{Mathf.RoundToInt(amount)}!"
                          : $"-{Mathf.RoundToInt(amount)}";
        tmp.fontSize = (isCrit && !isHeal) ? 52 : 42;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.fontStyle = FontStyles.Bold;

        if (font != null)
            tmp.font = font;

        baseColor = isHeal ? HealColor : (isCrit ? CritColor : DamageColor);
        tmp.color = baseColor;
        tmp.outlineWidth = 0.35f;
        tmp.outlineColor = isHeal ? HealOutline : (isCrit ? CritOutline : DamageOutline);

        // Drift direction — crits drift up to feel punchy
        float dir = (isHeal || isCrit) ? 1f : -1f;
        drift = new Vector3(Random.Range(-0.15f, 0.15f), dir * DriftSpeed, 0f);

        elapsed = 0f;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = elapsed / duration;

        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        // Movement — ease out
        float moveEase = 1f - (1f - t) * (1f - t);
        transform.position += drift * Time.deltaTime * (1f - moveEase * 0.5f);

        // Scale punch — snap back quickly
        float scaleT = Mathf.Min(elapsed / scalePunchDur, 1f);
        float scaleMul = Mathf.Lerp(scalePunch, 1f, scaleT * scaleT);
        transform.localScale = Vector3.one * startScaleMultiplier * scaleMul;

        // Fade — hold for first 40%, then fade out
        float alpha = t < 0.4f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.4f) / 0.6f);
        canvasGroup.alpha = alpha;
    }
}
