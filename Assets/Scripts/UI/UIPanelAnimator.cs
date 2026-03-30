using System.Collections;
using UnityEngine;

/// <summary>
/// Reusable panel open/close animations.
/// AnimateIn  — bounce-out (overshoots ~7% then settles).
/// AnimateOut — quick quadratic shrink to zero.
/// Both use unscaledDeltaTime so they work while the game is paused.
/// </summary>
public static class UIPanelAnimator
{
    public static IEnumerator AnimateIn(RectTransform rt, float duration = 0.28f)
    {
        rt.localScale = Vector3.zero;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Clamp01(t + Time.unscaledDeltaTime / duration);
            rt.localScale = Vector3.one * EaseOutBack(t);
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    public static IEnumerator AnimateOut(RectTransform rt, float duration = 0.15f)
    {
        float t = 1f;
        while (t > 0f)
        {
            t = Mathf.Clamp01(t - Time.unscaledDeltaTime / duration);
            float s = t * t;
            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        rt.localScale = Vector3.zero;
    }

    // Overshoot ease — peaks slightly above 1 before settling
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float x1 = x - 1f;
        return 1f + c3 * x1 * x1 * x1 + c1 * x1 * x1;
    }
}
