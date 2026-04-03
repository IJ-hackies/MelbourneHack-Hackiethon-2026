using UnityEngine;

/// <summary>
/// Tracks Pages Turned (floors cleared this run) and Furthest Page (PlayerPrefs best record).
/// Call RecordRun() at the moment of player death — it snapshots the previous record before saving.
/// </summary>
public static class PageTracker
{
    private const string PrefKey = "FurthestPage";

    /// <summary>Best record stored in PlayerPrefs.</summary>
    public static int FurthestPage => PlayerPrefs.GetInt(PrefKey, 0);

    /// <summary>Snapshot of FurthestPage taken just before RecordRun() updates it.</summary>
    public static int PreviousFurthestPage { get; private set; }

    /// <summary>
    /// Call at moment of death. Snapshots the current record into PreviousFurthestPage,
    /// then saves if pagesCleared beats it.
    /// </summary>
    public static void RecordRun(int pagesCleared)
    {
        PreviousFurthestPage = FurthestPage;
        if (pagesCleared > FurthestPage)
        {
            PlayerPrefs.SetInt(PrefKey, pagesCleared);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Converts a non-negative integer to a Roman numeral string. Returns "0" for zero.</summary>
    public static string ToRoman(int n)
    {
        if (n <= 0) return "0";

        var result = new System.Text.StringBuilder();
        int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] symbols = { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };

        for (int i = 0; i < values.Length; i++)
        {
            while (n >= values[i])
            {
                result.Append(symbols[i]);
                n -= values[i];
            }
        }
        return result.ToString();
    }
}
