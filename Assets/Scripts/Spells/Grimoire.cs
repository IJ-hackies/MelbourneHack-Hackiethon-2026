using System.Collections.Generic;
using UnityEngine;

public class Grimoire : MonoBehaviour
{
    public static Grimoire Instance { get; private set; }

    [Header("Starting Spell")]
    [SerializeField] private SpellData startingSpell;

    private List<SpellData> spells = new();
    private int activeIndex = 0;

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

    public SpellData ActiveSpell => spells.Count > 0 ? spells[activeIndex] : null;

    public IReadOnlyList<SpellData> AllSpells => spells;

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
}
