using System.Collections;
using UnityEngine;

/// <summary>
/// BonePile decoration — plays a shake animation when the player walks over it
/// or any spell projectile enters its trigger. Never breaks.
/// Requires a trigger Collider2D on this GameObject.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BonePileDeco : MonoBehaviour
{
    [SerializeField] private float shakeIntensity = 0.06f;
    [SerializeField] private float shakeDuration  = 0.35f;

    private Vector3    originalLocalPos;
    private bool       isShaking;
    private Coroutine  shakeRoutine;

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;

        if (GetComponent<Rigidbody2D>() == null)
        {
            var rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType    = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
        }
    }

    private void Start()
    {
        originalLocalPos = transform.localPosition;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isShaking) return;

        bool isPlayer     = other.CompareTag("Player");
        bool isProjectile = other.GetComponent<ProjectileHandler>() != null;

        if (!isPlayer && !isProjectile) return;

        shakeRoutine = StartCoroutine(ShakeRoutine());
    }

    private IEnumerator ShakeRoutine()
    {
        isShaking = true;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            float t = elapsed / shakeDuration;
            // Ease out — shake fades toward the end
            float intensity = shakeIntensity * (1f - t);
            transform.localPosition = originalLocalPos + new Vector3(
                Random.Range(-intensity, intensity),
                Random.Range(-intensity, intensity),
                0f);

            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = originalLocalPos;
        isShaking    = false;
        shakeRoutine = null;
    }

    private void OnDestroy()
    {
        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        transform.localPosition = originalLocalPos;
    }
}
