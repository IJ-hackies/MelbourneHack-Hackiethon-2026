using UnityEngine;

/// <summary>
/// Renders a purple pixel-perfect outline around the player sprite.
/// Auto-attached by PlayerAnimator.Awake — do not add manually.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerOutline : MonoBehaviour
{
    private SpriteRenderer _parentSr;
    private SpriteRenderer _outlineSr;
    private Material       _outlineMat;

    private void Awake()
    {
        _parentSr = GetComponent<SpriteRenderer>();

        var child = new GameObject("_Outline");
        child.transform.SetParent(transform, false);
        child.transform.localPosition = Vector3.zero;
        child.transform.localScale    = Vector3.one;

        _outlineSr                = child.AddComponent<SpriteRenderer>();
        _outlineSr.sortingLayerID = _parentSr.sortingLayerID;
        _outlineSr.sortingOrder   = _parentSr.sortingOrder - 1;

        var shader = Shader.Find("Custom/EnemyOutline");
        if (shader != null)
        {
            _outlineMat = new Material(shader);
            _outlineMat.SetColor("_OutlineColor", new Color(0.6f, 0.1f, 1f, 1f));
            _outlineMat.SetFloat("_OutlineSize", 0.5f);
            _outlineSr.material = _outlineMat;
        }
        else
        {
            Debug.LogWarning("[PlayerOutline] Shader 'Custom/EnemyOutline' not found.");
        }
    }

    private void LateUpdate()
    {
        if (_parentSr == null || _outlineSr == null) return;

        if (_outlineSr.sprite != _parentSr.sprite)
            _outlineSr.sprite = _parentSr.sprite;

        _outlineSr.sortingLayerID = _parentSr.sortingLayerID;
        _outlineSr.sortingOrder   = _parentSr.sortingOrder - 1;
    }

    private void OnDestroy()
    {
        if (_outlineMat != null)
            Destroy(_outlineMat);
    }
}
