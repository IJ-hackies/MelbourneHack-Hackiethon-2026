using System;
using System.Collections.Generic;
using UnityEngine;

public class Grimoire : MonoBehaviour
{
    public static Grimoire Instance { get; private set; }

    [Header("Starting Spell")]
    [SerializeField] private SpellData startingSpell;

    private List<SpellData> spells = new();
    private int activeIndex = 0;

    // Tracks spell names used this floor — assembled into the session log.
    private readonly HashSet<string> usedSpellNames = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (startingSpell != null)
            spells.Add(startingSpell);
    }

    // --- Active spell ---

    public SpellData ActiveSpell => spells.Count > 0 ? spells[activeIndex] : null;

    public IReadOnlyList<SpellData> AllSpells => spells;

    public void SetActiveSpell(int index)
    {
        if (index >= 0 && index < spells.Count)
            activeIndex = index;
    }

    public void SetActiveSpell(SpellData spell)
    {
        int idx = spells.IndexOf(spell);
        if (idx >= 0) activeIndex = idx;
    }

    // --- Collection management ---

    public void AddSpell(SpellData spell)
    {
        spells.Add(spell);
    }

    public void RemoveSpell(SpellData spell)
    {
        int idx = spells.IndexOf(spell);
        if (idx < 0) return;
        spells.RemoveAt(idx);
        activeIndex = Mathf.Clamp(activeIndex, 0, Mathf.Max(0, spells.Count - 1));
    }

    // --- Session log: spell usage tracking ---

    /// <summary>Called by SpellExecutor on every successful cast.</summary>
    public void RecordSpellUsed(SpellData spell)
    {
        if (spell != null)
            usedSpellNames.Add(spell.spellName);
    }

    /// <summary>Returns the names of all spells cast this floor.</summary>
    public IReadOnlyCollection<string> GetUsedSpellNames() => usedSpellNames;

    /// <summary>Call at the start of each new floor to reset the usage record.</summary>
    public void ResetUsedSpells() => usedSpellNames.Clear();

    // --- Floor Manifest: spell corruption ---

    /// <summary>
    /// Applies a corruption entry from the Floor Manifest to an existing spell.
    /// Adds/removes tags and updates flavor text in-place on the runtime SpellData.
    /// </summary>
    public void ApplyCorruption(CorruptionDTO dto)
    {
        SpellData target = null;
        foreach (var s in spells)
        {
            if (s.spellName == dto.spell_name) { target = s; break; }
        }

        if (target == null)
        {
            Debug.LogWarning($"Grimoire.ApplyCorruption: spell '{dto.spell_name}' not found.");
            return;
        }

        var tagList = new List<SpellTag>(target.tags ?? Array.Empty<SpellTag>());

        if (dto.added_tags != null)
        {
            foreach (string t in dto.added_tags)
            {
                if (Enum.TryParse(t, ignoreCase: true, out SpellTag tag) && !tagList.Contains(tag))
                    tagList.Add(tag);
                else if (!Enum.IsDefined(typeof(SpellTag), t))
                    Debug.LogWarning($"Grimoire.ApplyCorruption: unknown tag '{t}' in added_tags — skipped.");
            }
        }

        if (dto.removed_tags != null)
        {
            foreach (string t in dto.removed_tags)
            {
                if (Enum.TryParse(t, ignoreCase: true, out SpellTag tag))
                    tagList.Remove(tag);
            }
        }

        target.tags = tagList.ToArray();

        if (!string.IsNullOrEmpty(dto.new_flavor))
            target.flavor = dto.new_flavor;
    }

    // --- Merge Ritual ---

    /// <summary>
    /// Combines 2–3 source spells into a single merged spell, then removes the sources.
    /// <paramref name="mergedName"/> and <paramref name="mergedFlavor"/> come from the Gemini merge call.
    /// Returns the new merged SpellData, or null if fewer than 2 sources are provided.
    /// </summary>
    public SpellData MergeSpells(SpellData[] sources, string mergedName, string mergedFlavor)
    {
        if (sources == null || sources.Length < 2)
        {
            Debug.LogWarning("Grimoire.MergeSpells: need at least 2 source spells.");
            return null;
        }

        SpellData merged = ScriptableObject.CreateInstance<SpellData>();
        merged.spellName = mergedName;
        merged.flavor = mergedFlavor;
        merged.isMerged = true;
        merged.mergedFrom = Array.ConvertAll(sources, s => s.spellName);
        merged.element = sources[0].element;

        // Union all tags (no duplicates)
        var tagSet = new HashSet<SpellTag>();
        float totalDamage = 0f;
        float maxCooldown = 0f;
        float totalSpeed = 0f;

        foreach (var src in sources)
        {
            if (src.tags != null)
                foreach (var t in src.tags)
                    tagSet.Add(t);

            totalDamage += src.damage;
            if (src.cooldown > maxCooldown) maxCooldown = src.cooldown;
            totalSpeed += src.speed;
        }

        merged.tags = new SpellTag[tagSet.Count];
        tagSet.CopyTo(merged.tags);

        merged.damage = totalDamage;                       // additive — raw power increases
        merged.cooldown = maxCooldown * 1.5f;              // penalise for multi-fire
        merged.speed = totalSpeed / sources.Length;        // average

        foreach (var src in sources)
            RemoveSpell(src);

        AddSpell(merged);
        return merged;
    }
}
