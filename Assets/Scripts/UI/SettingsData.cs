using UnityEngine;

/// <summary>
/// Static store for all user settings — keybindings and volumes.
/// Always starts from defaults on launch (no PlayerPrefs restore).
/// Changes made during a session are applied live but not persisted across runs.
/// </summary>
public static class SettingsData
{
    // ── Keybindings ───────────────────────────────────────────────────────────
    public static KeyCode MoveUp    { get; private set; } = KeyCode.W;
    public static KeyCode MoveDown  { get; private set; } = KeyCode.S;
    public static KeyCode MoveLeft  { get; private set; } = KeyCode.A;
    public static KeyCode MoveRight { get; private set; } = KeyCode.D;
    public static KeyCode Attack        { get; private set; } = KeyCode.Mouse0;
    public static KeyCode SpellSkill    { get; private set; } = KeyCode.E;
    public static KeyCode SpellSkill2   { get; private set; } = KeyCode.Q;
    public static KeyCode SpellUltimate { get; private set; } = KeyCode.X;
    public static KeyCode Dash          { get; private set; } = KeyCode.LeftShift;
    public static KeyCode ToggleMap  { get; private set; } = KeyCode.M;

    // ── Volume ────────────────────────────────────────────────────────────────
    public static float MusicVolume { get; private set; } = 0.5f;
    public static float SfxVolume   { get; private set; } = 0.5f;

    /// <summary>Fired whenever any keybinding is changed via SetKey.</summary>
    public static event System.Action OnBindingsChanged;

    public static void SetKey(string action, KeyCode key)
    {
        switch (action)
        {
            case "MoveUp":    MoveUp    = key; break;
            case "MoveDown":  MoveDown  = key; break;
            case "MoveLeft":  MoveLeft  = key; break;
            case "MoveRight": MoveRight = key; break;
            case "Attack":        Attack        = key; break;
            case "SpellSkill":    SpellSkill    = key; break;
            case "SpellSkill2":   SpellSkill2   = key; break;
            case "SpellUltimate": SpellUltimate = key; break;
            case "Dash":          Dash          = key; break;
            case "ToggleMap": ToggleMap = key; break;
        }
        Save();
        OnBindingsChanged?.Invoke();
    }

    public static void SetMusicVolume(float v) { MusicVolume = Mathf.Clamp01(v); Save(); ApplyVolumes(); }
    public static void SetSfxVolume(float v)   { SfxVolume   = Mathf.Clamp01(v); Save(); ApplyVolumes(); }

    public static void ApplyVolumes()
    {
        MusicManager.Instance?.SetVolume(MusicVolume);
        SFXManager.Instance?.SetSfxVolume(SfxVolume);
    }

    public static void Save()
    {
        PlayerPrefs.SetInt("Key_MoveUp",    (int)MoveUp);
        PlayerPrefs.SetInt("Key_MoveDown",  (int)MoveDown);
        PlayerPrefs.SetInt("Key_MoveLeft",  (int)MoveLeft);
        PlayerPrefs.SetInt("Key_MoveRight", (int)MoveRight);
        PlayerPrefs.SetInt("Key_Attack",        (int)Attack);
        PlayerPrefs.SetInt("Key_SpellSkill",    (int)SpellSkill);
        PlayerPrefs.SetInt("Key_SpellSkill2",   (int)SpellSkill2);
        PlayerPrefs.SetInt("Key_SpellUltimate", (int)SpellUltimate);
        PlayerPrefs.SetInt("Key_Dash",          (int)Dash);
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
        Attack        = (KeyCode)PlayerPrefs.GetInt("Key_Attack",        (int)KeyCode.Mouse0);
        SpellSkill    = (KeyCode)PlayerPrefs.GetInt("Key_SpellSkill",    (int)KeyCode.E);
        SpellSkill2   = (KeyCode)PlayerPrefs.GetInt("Key_SpellSkill2",   (int)KeyCode.Q);
        SpellUltimate = (KeyCode)PlayerPrefs.GetInt("Key_SpellUltimate", (int)KeyCode.X);
        Dash          = (KeyCode)PlayerPrefs.GetInt("Key_Dash",          (int)KeyCode.LeftShift);
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
