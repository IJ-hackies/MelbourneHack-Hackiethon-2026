public enum SpellTag
{
    // --- Movement base type (one required per spell) ---
    PROJECTILE,
    ORBITAL,
    BEAM,
    CHANNELED,          // Steerable in real-time while mouse held; destroys on release

    // --- Trajectory modifiers ---
    HOMING,             // Curves toward nearest enemy
    WALL_BOUNCE,        // Reflects off walls, up to 3 times
    PIERCE_WALLS,       // Passes through wall geometry
    SPIRAL,             // Corkscrews through the air
    STUTTER_MOTION,     // Stop-start movement
    BOOMERANG,          // Travels out to max range then returns to caster
    REFLECTING,         // Bounces off walls indefinitely
    SURFACE_CRAWLING,   // Slides along wall surfaces instead of bouncing/destroying
    SKIPPING,           // Bounces periodically with scale pulse (top-down stone-skip)
    DELAYED_ARC,        // Magnifies at midpoint (arc peak), then accelerates forward
    SENTIENT,           // AI-controlled; hunts nearest enemy using pathfinding
    DELAYED,            // Fires but stays dormant for 1.2s before activating
    PHASING,            // Alternates in/out of reality every 0.5s (intangible while phased)
    PERSISTENT,         // No lifetime — stays in world until it hits something
    GHOST_CAST,         // Also fires an invisible damage copy alongside the visible spell
    MIRRORED,           // Also fires an identical copy from the opposite direction
    ECHOING,            // After 3s, automatically re-casts the spell once

    // --- On impact ---
    SPLIT_ON_IMPACT,    // Splits into 3 fixed-angle projectiles on hit (legacy)
    FRAGMENTING,        // Breaks into 4–6 chaotic random-angle projectiles on hit
    CHAIN,              // Jumps to nearest un-hit enemy within range after each hit
    LIFESTEAL,          // Heals caster for 30% of damage dealt
    AOE_BURST,          // Explodes for 50% damage in a 3-unit radius on hit
    PIERCE,             // Passes through enemies (up to 5 hits)
    DOUBLE_HIT,         // Fires a second copy at +15° offset
    PULL,               // Yanks hit enemy toward the caster
    PUSH,               // Knocks hit enemy away from the caster
    DETONATING,         // Embeds in enemy on first hit; explodes with 2x AoE after 2.5s
    LINGERING,          // Leaves a 5s damage zone where it lands or impacts
    SWAPPING,           // Teleports the caster to the projectile's impact point
    CONTAGIOUS,         // Spawns a 40%-damage copy from the hit enemy targeting nearby foes
    BURROWING,          // Goes underground after 0.5s; erupts with 1.5x AoE under nearest enemy

    // --- Caster interaction ---
    SACRIFICE,          // Costs 15% of current HP; deals 2× damage
    TETHERED,           // Embeds in enemy on hit; pulls them toward caster for 2.5s

    // --- Weird / meta ---
    PROBABILITY,        // Each cast randomly picks ONE behavior tag from the spell's tag list

    // --- Status effects ---
    SLOW,               // Reduces enemy move speed to 40% for 2s
    BURN,               // 10% damage/tick every 0.5s for 3s
    FREEZE,             // Stops movement and triples attack cooldown for 2s
    STUN,               // Stops movement and disables attacking for 1.5s
    POISON,             // 5%×stacks damage/s for 5s, stacks up to 3x
    BLEED,              // Escalating DoT: each tick deals more than the last, for 4s
    ROOT,               // Locks position for 2s but allows attacking (softer than STUN)
    WEAKNESS,           // Amplifies all incoming damage on target by 1.5× for 3s
    CURSE,              // Enemy flees from player for 3s (movement inverted)
    BLIND,              // Enemy loses player detection for 2.5s; wanders randomly

    // --- Corruption ---
    SELF_DAMAGE,        // Caster takes 20% of spell damage on cast
    ENEMY_HOMING,       // Projectile homes toward the player instead of enemies
    REVERSED_CONTROLS,  // Aim direction is flipped on cast
}
