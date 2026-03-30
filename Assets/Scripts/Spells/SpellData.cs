using UnityEngine;

[CreateAssetMenu(fileName = "NewSpell", menuName = "Grimoire/Spell")]
public class SpellData : ScriptableObject
{
    [Header("Identity")]
    public string spellName;
    [TextArea] public string flavor;
    [TextArea] public string corruptionFlavor;
    public string element;

    [Header("Behavior")]
    public SpellTag[] tags;

    [Header("Stats")]
    public float damage;
    public float speed;
    public float cooldown;

    [Header("Icon")]
    public Sprite icon;              // spell icon sprite (loaded from Resources or generated via Nano Banana)

    [Header("Visuals")]
    public string projectileColor;   // hex e.g. "#FF4400", overrides element default
    public string secondaryColor;    // hex for trail/glow gradient
    public float  projectileScale;   // 0.5–3.0, multiplier on transform.localScale
    public float  glowSize;          // 0.2–1.5, world-unit radius of glow sprite
    public float  trailLength;       // 0.0–0.5, trail time in seconds (0 = no trail)
    public float  trailWidth;        // 0.05–0.5, trail start width
    public int    burstCount;        // 1–5, number of projectiles fired in a spread

    [Header("Merge")]
    public bool isMerged;
    public string[] mergedFrom;
    [HideInInspector] public SpellData[] mergedSourceSpells;

    public bool HasTag(SpellTag tag)
    {
        if (tags == null) return false;
        foreach (var t in tags)
            if (t == tag) return true;
        return false;
    }
}
