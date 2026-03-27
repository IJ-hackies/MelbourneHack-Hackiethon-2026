using System;
using UnityEngine;

/// <summary>
/// Plain C# class matching the "new_spell" block in Gemini's Floor Manifest JSON.
/// No Unity dependencies — safe to deserialize with any JSON library.
/// Convert to a runtime SpellData asset via ToSpellData().
/// </summary>
[Serializable]
public class SpellDTO
{
    public string name;
    public string flavor;
    public string[] tags;
    public float damage;
    public float speed;
    public float cooldown;
    public string element;
    public bool is_merged;
    public string[] merged_from;

    /// <summary>
    /// Creates a runtime SpellData ScriptableObject from this DTO.
    /// The returned instance is not saved to disk — it lives only for this run.
    /// </summary>
    public SpellData ToSpellData()
    {
        SpellData data = ScriptableObject.CreateInstance<SpellData>();
        data.spellName = name;
        data.flavor = flavor;
        data.element = element;
        data.damage = damage;
        data.speed = speed;
        data.cooldown = cooldown;
        data.isMerged = is_merged;
        data.mergedFrom = merged_from ?? Array.Empty<string>();

        // Parse string tags → SpellTag enum, skip unknowns gracefully
        if (tags != null)
        {
            var parsed = new System.Collections.Generic.List<SpellTag>();
            foreach (string t in tags)
            {
                if (Enum.TryParse(t, ignoreCase: true, out SpellTag tag))
                    parsed.Add(tag);
                else
                    Debug.LogWarning($"SpellDTO: unknown tag '{t}' on spell '{name}' — skipped.");
            }
            data.tags = parsed.ToArray();
        }

        return data;
    }
}
