using UnityEngine;

// Attached to slow-indicator arrow sprites. Drifts the arrow downward in a looping
// animation to visually communicate that the player's movement is being dragged down.
public class SlowArrowBobber : MonoBehaviour
{
    [SerializeField] private float amplitude = 0.06f; // bob range in world units
    [SerializeField] private float frequency = 0.4f;  // cycles per second
    [SerializeField] private float phaseOffset = 0f;  // set at spawn to stagger arrows

    private Vector3 baseLocalPos;

    private void OnEnable()
    {
        baseLocalPos = transform.localPosition;
        // Random phase so the two arrows don't bob in sync
        phaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        float t = Mathf.Sin(Time.time * frequency * Mathf.PI * 2f + phaseOffset);
        transform.localPosition = baseLocalPos + Vector3.down * ((t + 1f) * 0.5f) * amplitude;
    }
}
