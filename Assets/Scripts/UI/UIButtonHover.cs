using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to any UI button GameObject to get hover magnify + click compress/spring.
/// Uses only localScale — safe alongside ButtonLevitate (no position conflict).
/// Added via AddComponent after creating each Button.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UIButtonHover : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    public float hoverScale = 1.10f;
    public float clickScale = 0.90f;
    public float speed      = 18f;

    private RectTransform _rt;
    private float _target = 1f;
    private bool  _over, _down;

    private void Awake() => _rt = GetComponent<RectTransform>();

    private void Update()
    {
        float s = Mathf.Lerp(_rt.localScale.x, _target, Time.unscaledDeltaTime * speed);
        _rt.localScale = new Vector3(s, s, 1f);
    }

    public void OnPointerEnter(PointerEventData _) { _over = true;  if (!_down) _target = hoverScale; }
    public void OnPointerExit (PointerEventData _) { _over = false; if (!_down) _target = 1f; }
    public void OnPointerDown (PointerEventData _) { _down = true;  _target = clickScale; }
    public void OnPointerUp   (PointerEventData _) { _down = false; _target = _over ? hoverScale : 1f; }

    private void OnDisable()
    {
        _over = _down = false;
        _target = 1f;
        if (_rt != null) _rt.localScale = Vector3.one;
    }
}
