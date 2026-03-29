using System;
using UnityEngine;

/// <summary>
/// Monitors living enemy count for the current floor.
/// EnemySpawner calls RegisterEnemy() for each spawn; Health.OnDeath decrements.
/// Fires OnFloorCleared once all registered enemies are dead.
/// Also fires OnEnemyCountChanged with (alive, total) for pre-generation triggers.
/// </summary>
public class FloorClearDetector : MonoBehaviour
{
    public static FloorClearDetector Instance { get; private set; }

    /// <summary>Fired once when all registered enemies are dead. Not fired if total was 0.</summary>
    public event Action OnFloorCleared;

    /// <summary>Fired every time an enemy dies. Args: (alive, total).</summary>
    public event Action<int, int> OnEnemyCountChanged;

    private int totalEnemies;
    private int aliveEnemies;
    private bool cleared;

    public int TotalEnemies => totalEnemies;
    public int AliveEnemies => aliveEnemies;
    public bool IsCleared   => cleared;

    /// <summary>Fraction of enemies killed (0..1). Returns 0 if none spawned yet.</summary>
    public float KillProgress => totalEnemies > 0 ? 1f - (float)aliveEnemies / totalEnemies : 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Call at the start of each new floor before enemies spawn.</summary>
    public void Reset()
    {
        totalEnemies = 0;
        aliveEnemies = 0;
        cleared      = false;
    }

    /// <summary>Called by EnemySpawner for each enemy instantiated.</summary>
    public void RegisterEnemy(GameObject enemy)
    {
        var health = enemy.GetComponent<Health>();
        if (health == null)
        {
            Debug.LogWarning($"FloorClearDetector: enemy '{enemy.name}' has no Health component.");
            return;
        }

        totalEnemies++;
        aliveEnemies++;
        health.OnDeath.AddListener(() => OnEnemyDied(enemy));
    }

    private void OnEnemyDied(GameObject enemy)
    {
        aliveEnemies = Mathf.Max(0, aliveEnemies - 1);
        OnEnemyCountChanged?.Invoke(aliveEnemies, totalEnemies);

        if (aliveEnemies <= 0 && totalEnemies > 0 && !cleared)
        {
            cleared = true;
            OnFloorCleared?.Invoke();
        }
    }
}
