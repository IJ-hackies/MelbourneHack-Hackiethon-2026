using UnityEngine;

/// <summary>
/// Renders a clean 1-pixel black outline around a decoration or lamp sprite.
/// Works by placing four black-tinted copies of the sprite offset by 1 pixel
/// in each cardinal direction, one sorting order behind the original.
/// No custom shader required — works on any sprite type.
///
/// Attach to the root of any deco or lamp prefab.
/// </summary>
public class DecoOutline : MonoBehaviour
{
    private SpriteRenderer   _parentSr;
    private SpriteRenderer[] _layers;   // N, S, E, W

    private static readonly Vector2[] Directions =
    {
        Vector2.up, Vector2.down, Vector2.right, Vector2.left
    };

    private void Awake()
    {
        _parentSr = FindMainSprite();
        if (_parentSr == null)
        {
            Debug.LogWarning($"[DecoOutline] No SpriteRenderer found on '{name}' or children.");
            return;
        }

        _layers = new SpriteRenderer[4];
        for (int i = 0; i < 4; i++)
        {
            var child = new GameObject($"_Outline{i}");
            child.transform.SetParent(_parentSr.transform, false);
            child.transform.localScale = Vector3.one;

            var sr = child.AddComponent<SpriteRenderer>();
            sr.sprite         = _parentSr.sprite;
            sr.color          = Color.black;
            sr.sortingLayerID = _parentSr.sortingLayerID;
            sr.sortingOrder   = _parentSr.sortingOrder - 1;
            _layers[i]        = sr;
        }

        UpdateOffsets();
    }

    private void LateUpdate()
    {
        if (_parentSr == null || _layers == null) return;

        for (int i = 0; i < 4; i++)
        {
            if (_layers[i] == null) continue;
            // Sync sprite in case it changes (e.g. animated decos)
            if (_layers[i].sprite != _parentSr.sprite)
                _layers[i].sprite = _parentSr.sprite;

            // Stay one order behind — accounts for YSorter updating every frame
            _layers[i].sortingLayerID = _parentSr.sortingLayerID;
            _layers[i].sortingOrder   = _parentSr.sortingOrder - 1;
        }

        UpdateOffsets();
    }

    /// <summary>
    /// Recalculates the 1-pixel offset in world units based on the sprite's PPU.
    /// </summary>
    private void UpdateOffsets()
    {
        if (_parentSr == null || _parentSr.sprite == null) return;

        float d = 1f / _parentSr.sprite.pixelsPerUnit;
        for (int i = 0; i < 4; i++)
        {
            if (_layers[i] == null) continue;
            _layers[i].transform.localPosition = (Vector3)(Directions[i] * d);
        }
    }

    /// <summary>
    /// Prefers the SpriteRenderer on the root; falls back to the largest one in children.
    /// </summary>
    private SpriteRenderer FindMainSprite()
    {
        var root = GetComponent<SpriteRenderer>();
        if (root != null) return root;

        var all = GetComponentsInChildren<SpriteRenderer>();
        if (all.Length == 0) return null;
        if (all.Length == 1) return all[0];

        SpriteRenderer best     = all[0];
        float          bestArea = 0f;
        foreach (var sr in all)
        {
            if (sr.sprite == null) continue;
            float area = sr.sprite.bounds.size.x * sr.sprite.bounds.size.y;
            if (area > bestArea) { bestArea = area; best = sr; }
        }
        return best;
    }
}
