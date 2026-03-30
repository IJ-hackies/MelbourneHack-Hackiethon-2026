using UnityEngine;

/// <summary>
/// Renders a red pixel-perfect outline around an enemy sprite.
/// Attach automatically via EnemyBase.Awake — do not add manually.
/// The outline is drawn on a child SpriteRenderer one sorting-order below
/// the parent, using Custom/EnemyOutline shader to sample neighbouring pixels.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class EnemyOutline : MonoBehaviour
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
            _outlineMat.SetColor("_OutlineColor", new Color(1f, 0.1f, 0.1f, 1f));
            _outlineMat.SetFloat("_OutlineSize", 0.5f);
            _outlineSr.material = _outlineMat;
        }
        else
        {
            Debug.LogWarning("[EnemyOutline] Shader 'Custom/EnemyOutline' not found.");
        }

        // Disable on death so the outline doesn't linger during the dissolve.
        var health = GetComponent<Health>();
        if (health != null)
            health.OnDeath.AddListener(HideOutline);
    }

    private void LateUpdate()
    {
        if (_parentSr == null || _outlineSr == null) return;

        if (_outlineSr.sprite != _parentSr.sprite)
            _outlineSr.sprite = _parentSr.sprite;

        // Keep sorting order in sync in case it ever changes at runtime.
        _outlineSr.sortingLayerID = _parentSr.sortingLayerID;
        _outlineSr.sortingOrder   = _parentSr.sortingOrder - 1;
    }

    private void HideOutline()
    {
        if (_outlineSr != null)
            _outlineSr.enabled = false;
    }

    private void OnDestroy()
    {
        if (_outlineMat != null)
            Destroy(_outlineMat);
    }
}
