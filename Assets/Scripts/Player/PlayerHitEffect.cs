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
        flashCoroutine = StartCoroutine(Flash(flashColor));
    }

    // Called by HealScrollPickup on heal
    public void PlayHealEffect()
    {
        HitEffectSpawner.SpawnHit(transform.position,
            new Color(0.2f, 1f, 0.35f, 1f),
            new Color(0.6f, 1f, 0.2f, 1f));

        if (flashCoroutine != null)
            StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(Flash(new Color(0.2f, 1f, 0.35f, 1f)));
    }

    private IEnumerator Flash(Color color)
    {
        sr.color = color;
        yield return new WaitForSeconds(flashDuration);
        sr.color = originalColor;
    }
}
