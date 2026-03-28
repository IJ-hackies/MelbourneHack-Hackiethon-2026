using UnityEngine;

/// <summary>
/// Adds a drop shadow beneath any animated sprite.
/// Attach to the same GameObject as the SpriteRenderer (and YSorter).
/// The shadow child is created automatically — no prefab needed.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteShadow : MonoBehaviour
{
    [Tooltip("Horizontal offset of the shadow (positive = right).")]
    public float offsetX = 0.12f;

    [Tooltip("Vertical offset of the shadow (negative = below).")]
    public float offsetY = -0.2f;

    [Tooltip("Shadow darkness — 0 invisible, 1 fully opaque black.")]
    [Range(0f, 1f)]
    public float alpha = 0.45f;

    [Tooltip("Uniform scale applied to the shadow sprite (slightly squashed looks grounded).")]
    public Vector2 shadowScale = new Vector2(0.85f, 0.5f);

    private SpriteRenderer sourceSR;
    private SpriteRenderer shadowSR;

    private void Awake()
    {
        sourceSR = GetComponent<SpriteRenderer>();

        var go = new GameObject("_Shadow");
        go.transform.SetParent(transform);
        go.transform.localPosition  = new Vector3(offsetX, offsetY, 0f);
        go.transform.localScale     = new Vector3(shadowScale.x, shadowScale.y, 1f);
        go.transform.localRotation  = Quaternion.identity;

        shadowSR = go.AddComponent<SpriteRenderer>();
        shadowSR.color          = new Color(0f, 0f, 0f, alpha);
        shadowSR.sortingLayerID = sourceSR.sortingLayerID;
    }

    private void LateUpdate()
    {
        // Mirror the animated sprite every frame
        shadowSR.sprite          = sourceSR.sprite;
        shadowSR.flipX           = sourceSR.flipX;
        shadowSR.flipY           = sourceSR.flipY;

        // Always one step behind the source (YSorter updates source first)
        shadowSR.sortingOrder    = sourceSR.sortingOrder - 1;

        // Keep offset in world space regardless of parent transform changes
        shadowSR.transform.localPosition = new Vector3(offsetX, offsetY, 0f);

        // Sync alpha in case it's tweaked in Inspector at runtime
        var c    = shadowSR.color;
        c.a      = alpha;
        shadowSR.color = c;
    }
}
