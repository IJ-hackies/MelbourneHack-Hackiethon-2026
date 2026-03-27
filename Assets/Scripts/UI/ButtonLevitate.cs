using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to any UI button RectTransform to give it a gentle idle levitating bob.
/// Waits one frame before capturing the origin so Unity's layout pass has finished.
/// </summary>
public class ButtonLevitate : MonoBehaviour
{
    [Header("Levitation")]
    [SerializeField] private float amplitude  = 8f;   // pixels up/down
    [SerializeField] private float speed      = 1.4f; // cycles per second
    [SerializeField] private float phaseOffset = 0f;  // radians — randomised in Awake if 0

    private RectTransform _rect;
    private Vector2 _originPos;
    private bool _ready;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
    }

    private IEnumerator Start()
    {
        // Wait one frame so the Canvas layout system has placed the button correctly.
        yield return null;
        _originPos = _rect.anchoredPosition;
        _ready = true;
    }

    private void Update()
    {
        if (!_ready) return;
        float y = Mathf.Sin(Time.time * speed * Mathf.PI * 2f + phaseOffset) * amplitude;
        _rect.anchoredPosition = _originPos + new Vector2(0f, y);
    }
}
