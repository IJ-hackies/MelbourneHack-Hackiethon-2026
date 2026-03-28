using System.Collections;
using UnityEngine;

/// <summary>
/// Added at runtime by EnemySpawner when the "regenerating" modifier is applied.
/// Heals the enemy for a small amount each tick while alive.
/// </summary>
[RequireComponent(typeof(Health))]
public class EnemyRegen : MonoBehaviour
{
    [SerializeField] private float healPerSecond = 5f;
    [SerializeField] private float tickInterval  = 0.5f;

    private Health _health;

    private void Awake()
    {
        _health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        StartCoroutine(RegenLoop());
    }

    private IEnumerator RegenLoop()
    {
        var wait = new WaitForSeconds(tickInterval);
        while (!_health.IsDead)
        {
            _health.Heal(healPerSecond * tickInterval);
            yield return wait;
        }
    }
}
