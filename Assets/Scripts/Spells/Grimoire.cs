using System;
using System.Collections.Generic;
using UnityEngine;

public class Grimoire : MonoBehaviour
{
    public static Grimoire Instance { get; private set; }

    public const int LoadoutSize = 3;

    [Header("Starting Spell")]
    [SerializeField] private SpellData startingSpell;

    // Full spell library — every spell the player owns.
    private List<SpellData> library = new();

    // Equipped loadout — 3 slots, null = empty.
    private SpellData[] loadout = new SpellData[LoadoutSize];

    // Which loadout slot is currently active (0-2).
    private int activeSlot = 0;

    // Per-slot cooldown tracking (time of last cast).
    private float[] lastCastTimes = new float[LoadoutSize];

    // Tracks spell names used this floor — assembled into the session log.
    private readonly HashSet<string> usedSpellNames = new();

    /// <summary>Fired whenever the loadout or active slot changes. UI subscribes to this.</summary>
    public event Action OnLoadoutChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        for (int i = 0; i < LoadoutSize; i++)
            lastCastTimes[i] = -999f;

        // When StageDirector is present, it handles the initial spell via the Stage 1 manifest.
        // Only add startingSpell if running standalone (e.g. testing without StageDirector).
        if (startingSpell != null && FindAnyObjectByType<StageDirector>() == null)
            AddSpell(startingSpell);
    }

    private void Update()
    {
        // 1/2/3 key switching
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetActiveSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetActiveSlot(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetActiveSlot(2);
    }

    // --- Active spell (reads from loadout) ---

    public int ActiveSlot => activeSlot;
    public SpellData ActiveSpell => loadout[activeSlot];
    public SpellData[] Loadout => loadout;

    /// <summary>All spells the player owns (library).</summary>
    public IReadOnlyList<SpellData> AllSpells => library;

    public void SetActiveSlot(int slot)
    {
        if (slot < 0 || slot >= LoadoutSize) return;
        if (slot == activeSlot) return;
        activeSlot = slot;
        OnLoadoutChanged?.Invoke();
    }

    // --- Loadout management ---

    /// <summary>Equip a spell from the library into a specific loadout slot.</summary>
    public void EquipToSlot(SpellData spell, int slot)
    {
        if (slot < 0 || slot >= LoadoutSize) return;
        if (!library.Contains(spell)) return;

        // If this spell is already in another slot, clear that slot first
        for (int i = 0; i < LoadoutSize; i++)
            if (loadout[i] == spell) loadout[i] = null;

        loadout[slot] = spell;
        OnLoadoutChanged?.Invoke();
    }

    /// <summary>Remove a spell from its loadout slot (unequip, stays in library).</summary>
    public void UnequipSlot(int slot)
    {
        if (slot < 0 || slot >= LoadoutSize) return;
        loadout[slot] = null;
        OnLoadoutChanged?.Invoke();
    }

    /// <summary>Auto-equip a spell into the first empty loadout slot. Returns the slot index, or -1 if full.</summary>
    private int AutoEquip(SpellData spell)
    {
        for (int i = 0; i < LoadoutSize; i++)
        {
            if (loadout[i] == null)
            {
                loadout[i] = spell;
                OnLoadoutChanged?.Invoke();
                return i;
            }
        }
        return -1;
    }

    // --- Cooldown tracking (per-slot) ---

    public void RecordCast(int slot) => lastCastTimes[slot] = Time.time;
    public float GetLastCastTime(int slot) => lastCastTimes[slot];

    public float GetCooldownRemaining(int slot)
    {
        SpellData spell = loadout[slot];
        if (spell == null) return 0f;
        float elapsed = Time.time - lastCastTimes[slot];
        return Mathf.Max(0f, spell.cooldown - elapsed);
    }

    public bool IsSlotReady(int slot)
    {
        return loadout[slot] != null && GetCooldownRemaining(slot) <= 0f;
    }

    // --- Collection management ---

    public void AddSpell(SpellData spell)
    {
        library.Add(spell);
        AutoEquip(spell);
    }

    public void RemoveSpell(SpellData spell)
    {
        int idx = library.IndexOf(spell);
        if (idx < 0) return;
        library.RemoveAt(idx);

        // Clear from loadout if equipped
        for (int i = 0; i < LoadoutSize; i++)
            if (loadout[i] == spell) loadout[i] = null;

        // Clamp active slot to a non-null slot if possible
        if (loadout[activeSlot] == null)
        {
            for (int i = 0; i < LoadoutSize; i++)
                if (loadout[i] != null) { activeSlot = i; break; }
        }
        OnLoadoutChanged?.Invoke();
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
        foreach (var s in library)
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

        // Inherit visuals from first source, scale up slightly for merged power
        merged.projectileColor = sources[0].projectileColor;
        merged.secondaryColor  = sources[0].secondaryColor;
        merged.projectileScale = Mathf.Min(sources[0].projectileScale * 1.3f, 4f);
        merged.glowSize        = Mathf.Min(sources[0].glowSize * 1.3f, 2f);
        merged.trailLength     = Mathf.Min(sources[0].trailLength * 1.2f, 0.8f);
        merged.trailWidth      = Mathf.Min(sources[0].trailWidth * 1.2f, 0.8f);
        merged.burstCount      = Mathf.Max(sources[0].burstCount, 1);

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
