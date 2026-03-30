using UnityEngine;

/// <summary>
/// Scrolls a SpriteRenderer's material UV offset to simulate fluid motion within the sprite.
/// Attach to AcidPool and WaterPool deco prefabs.
/// NOTE: The sprite's texture must have Wrap Mode set to "Repeat" in its import settings,
/// otherwise the UV scroll will not be visible.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class FluidAnimator : MonoBehaviour
{
    [SerializeField] private Vector2 scrollSpeed = new Vector2(0.12f, 0.08f);

    private Material _material;

    private void Start()
    {
        var sr = GetComponent<SpriteRenderer>();
        // Instantiate own material so UV offset doesn't affect shared sprite atlas
        _material = new Material(sr.sharedMaterial);
        sr.material = _material;
    }

    private void Update()
    {
        _material.mainTextureOffset += scrollSpeed * Time.deltaTime;
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}
