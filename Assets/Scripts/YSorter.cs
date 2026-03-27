using UnityEngine;

// Attach to any GameObject with a SpriteRenderer.
// Automatically sets sortingOrder based on Y position so lower = in front.
// Use sortingOrigin to offset the sort point to the character's feet.
[RequireComponent(typeof(SpriteRenderer))]
[ExecuteAlways] // works in editor too
public class YSorter : MonoBehaviour
{
    [Tooltip("Pixels per unit precision. Higher = finer sorting granularity.")]
    [SerializeField] private float precision = 100f;

    [Tooltip("World-space Y offset from pivot to the sort point (e.g. move down to sort from feet).")]
    [SerializeField] private float sortingOriginY = 0f;

    private SpriteRenderer sr;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    private void LateUpdate()
    {
        sr.sortingOrder = Mathf.RoundToInt(-(transform.position.y + sortingOriginY) * precision);
    }
}
