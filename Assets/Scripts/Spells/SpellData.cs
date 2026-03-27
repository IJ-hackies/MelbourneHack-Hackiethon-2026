using UnityEngine;

[CreateAssetMenu(fileName = "NewSpell", menuName = "Grimoire/Spell")]
public class SpellData : ScriptableObject
{
    [Header("Identity")]
    public string spellName;
    [TextArea] public string flavor;
    public string element;

    [Header("Behavior")]
    public SpellTag[] tags;

    [Header("Stats")]
    public float damage;
    public float speed;
    public float cooldown;

    [Header("Merge")]
    public bool isMerged;
    public string[] mergedFrom;

    public bool HasTag(SpellTag tag)
    {
        if (tags == null) return false;
        foreach (var t in tags)
            if (t == tag) return true;
        return false;
    }
}
