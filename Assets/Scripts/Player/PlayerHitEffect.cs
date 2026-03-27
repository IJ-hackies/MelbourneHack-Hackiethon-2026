using System.Collections;
using UnityEngine;

// Attach to the Player. Call PlayHitEffect() from the enemy after its own delay.
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerHitEffect : MonoBehaviour
{
    [SerializeField] private float flashDuration = 0.12f;
    [SerializeField] private Color flashColor = new Color(1f, 0.15f, 0.15f, 1f);

    private SpriteRenderer sr;
    private Color originalColor;
    private Coroutine flashCoroutine;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        originalColor = sr.color;
    }

    // Called by the enemy script after its attack-timing delay
    public void PlayHitEffect()
    {
        HitEffectSpawner.SpawnBlood(transform.position);

        if (flashCoroutine != null)
            StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(Flash());
    }

    private IEnumerator Flash()
    {
        sr.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        sr.color = originalColor;
    }
}
