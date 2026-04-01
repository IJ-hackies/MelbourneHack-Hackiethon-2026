/// <summary>
/// Spell tiers: each tier maps to a dedicated input key and slot in the loadout.
///   Basic    → LMB (held)      — fast, spammable bread-and-butter
///   Skill    → E (pressed)     — tactical, medium cooldown
///   Ultimate → Q (pressed)     — cinematic, long cooldown, massive power
/// </summary>
public enum SpellTier
{
    Basic    = 0,
    Skill    = 1,
    Ultimate = 2,
}
