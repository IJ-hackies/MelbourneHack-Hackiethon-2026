using UnityEngine;

/// <summary>
/// Static pause helper. Any overlay UI calls Pause/Unpause.
/// Tracks nested pause requests so overlays don't fight each other.
/// </summary>
public static class PauseManager
{
    private static int pauseCount = 0;

    public static bool IsPaused => pauseCount > 0;

    public static void Pause()
    {
        pauseCount++;
        Time.timeScale = 0f;
    }

    public static void Unpause()
    {
        pauseCount = Mathf.Max(0, pauseCount - 1);
        if (pauseCount == 0)
            Time.timeScale = 1f;
    }

    /// <summary>Force-reset to unpaused state (e.g. on scene load).</summary>
    public static void Reset()
    {
        pauseCount = 0;
        Time.timeScale = 1f;
    }
}
