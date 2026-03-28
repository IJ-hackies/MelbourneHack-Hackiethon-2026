using System;

/// <summary>
/// Matches one entry in the "corrupted_spells" array of Gemini's Floor Manifest JSON.
/// Pass to Grimoire.ApplyCorruption() to mutate an existing spell at runtime.
/// </summary>
[Serializable]
public class CorruptionDTO
{
    public string spell_name;
    public string[] added_tags;
    public string[] removed_tags;
    public string new_flavor;
}
