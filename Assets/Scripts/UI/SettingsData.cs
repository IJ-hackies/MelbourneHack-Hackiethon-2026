using UnityEngine;

/// <summary>
/// Static store for all user settings — keybindings and volumes.
/// Persisted via PlayerPrefs. Access from anywhere; call Save() after mutations.
/// </summary>
public static class SettingsData
{
    // ── Keybindings ───────────────────────────────────────────────────────────
    public static KeyCode MoveUp    { get; private set; } = KeyCode.W;
    public static KeyCode MoveDown  { get; private set; } = KeyCode.S;
    public static KeyCode MoveLeft  { get; private set; } = KeyCode.A;
    public static KeyCode MoveRight { get; private set; } = KeyCode.D;
    public static KeyCode Attack    { get; private set; } = KeyCode.Mouse0;
    public static KeyCode Slot1      { get; private set; } = KeyCode.Alpha1;
    public static KeyCode Slot2      { get; private set; } = KeyCode.Alpha2;
    public static KeyCode Slot3      { get; private set; } = KeyCode.Alpha3;
    public static KeyCode Dash       { get; private set; } = KeyCode.E;
    public static KeyCode ToggleMap  { get; private set; } = KeyCode.M;

    // ── Volume ────────────────────────────────────────────────────────────────
    public static float MusicVolume { get; private set; } = 0.8f;
    public static float SfxVolume   { get; private set; } = 0.8f;

    /// <summary>Fired whenever any keybinding is changed via SetKey.</summary>
    public static event System.Action OnBindingsChanged;

    static SettingsData() => Load();

    public static void SetKey(string action, KeyCode key)
    {
        switch (action)
        {
            case "MoveUp":    MoveUp    = key; break;
            case "MoveDown":  MoveDown  = key; break;
            case "MoveLeft":  MoveLeft  = key; break;
            case "MoveRight": MoveRight = key; break;
            case "Attack":    Attack    = key; break;
            case "Slot1":     Slot1     = key; break;
            case "Slot2":     Slot2     = key; break;
            case "Slot3":     Slot3     = key; break;
            case "Dash":      Dash      = key; break;
            case "ToggleMap": ToggleMap = key; break;
        }
        Save();
        OnBindingsChanged?.Invoke();
    }

    public static void SetMusicVolume(float v) { MusicVolume = Mathf.Clamp01(v); Save(); ApplyVolumes(); }
    public static void SetSfxVolume(float v)   { SfxVolume   = Mathf.Clamp01(v); Save(); }

    public static void ApplyVolumes()
    {
        AudioListener.volume = MusicVolume;
    }

    public static void Save()
    {
        PlayerPrefs.SetInt("Key_MoveUp",    (int)MoveUp);
        PlayerPrefs.SetInt("Key_MoveDown",  (int)MoveDown);
        PlayerPrefs.SetInt("Key_MoveLeft",  (int)MoveLeft);
        PlayerPrefs.SetInt("Key_MoveRight", (int)MoveRight);
        PlayerPrefs.SetInt("Key_Attack",    (int)Attack);
        PlayerPrefs.SetInt("Key_Slot1",     (int)Slot1);
        PlayerPrefs.SetInt("Key_Slot2",     (int)Slot2);
        PlayerPrefs.SetInt("Key_Slot3",     (int)Slot3);
        PlayerPrefs.SetInt("Key_Dash",      (int)Dash);
        PlayerPrefs.SetInt("Key_ToggleMap", (int)ToggleMap);
        PlayerPrefs.SetFloat("Vol_Music",   MusicVolume);
        PlayerPrefs.SetFloat("Vol_Sfx",     SfxVolume);
        PlayerPrefs.Save();
    }

    public static void Load()
    {
        MoveUp    = (KeyCode)PlayerPrefs.GetInt("Key_MoveUp",    (int)KeyCode.W);
        MoveDown  = (KeyCode)PlayerPrefs.GetInt("Key_MoveDown",  (int)KeyCode.S);
        MoveLeft  = (KeyCode)PlayerPrefs.GetInt("Key_MoveLeft",  (int)KeyCode.A);
        MoveRight = (KeyCode)PlayerPrefs.GetInt("Key_MoveRight", (int)KeyCode.D);
        Attack    = (KeyCode)PlayerPrefs.GetInt("Key_Attack",    (int)KeyCode.Mouse0);
        Slot1     = (KeyCode)PlayerPrefs.GetInt("Key_Slot1",     (int)KeyCode.Alpha1);
        Slot2     = (KeyCode)PlayerPrefs.GetInt("Key_Slot2",     (int)KeyCode.Alpha2);
        Slot3     = (KeyCode)PlayerPrefs.GetInt("Key_Slot3",     (int)KeyCode.Alpha3);
        Dash      = (KeyCode)PlayerPrefs.GetInt("Key_Dash",      (int)KeyCode.E);
        ToggleMap = (KeyCode)PlayerPrefs.GetInt("Key_ToggleMap", (int)KeyCode.M);
        MusicVolume = PlayerPrefs.GetFloat("Vol_Music", 0.8f);
        SfxVolume   = PlayerPrefs.GetFloat("Vol_Sfx",   0.8f);
        ApplyVolumes();
    }

    /// <summary>Human-readable label for a KeyCode.</summary>
    public static string KeyLabel(KeyCode k) => k switch
    {
        KeyCode.Mouse0       => "LMB",
        KeyCode.Mouse1       => "RMB",
        KeyCode.Mouse2       => "MMB",
        KeyCode.Alpha0       => "0",
        KeyCode.Alpha1       => "1",
        KeyCode.Alpha2       => "2",
        KeyCode.Alpha3       => "3",
        KeyCode.Alpha4       => "4",
        KeyCode.Alpha5       => "5",
        KeyCode.Alpha6       => "6",
        KeyCode.Alpha7       => "7",
        KeyCode.Alpha8       => "8",
        KeyCode.Alpha9       => "9",
        KeyCode.Return       => "Enter",
        KeyCode.LeftShift    => "L.Shift",
        KeyCode.RightShift   => "R.Shift",
        KeyCode.LeftControl  => "L.Ctrl",
        KeyCode.RightControl => "R.Ctrl",
        KeyCode.LeftAlt      => "L.Alt",
        KeyCode.RightAlt     => "R.Alt",
        KeyCode.Space        => "Space",
        KeyCode.Tab          => "Tab",
        KeyCode.Backspace    => "Bksp",
        KeyCode.Delete       => "Del",
        KeyCode.UpArrow      => "Up",
        KeyCode.DownArrow    => "Down",
        KeyCode.LeftArrow    => "Left",
        KeyCode.RightArrow   => "Right",
        _                    => k.ToString().ToUpper(),
    };
}
