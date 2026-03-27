## HACKATHON PROJECT — THEME: "INTEGRATE LLM INTO A GAME"

## The Concept: "Everchanging Grimoire"

A 2D top-down roguelite dungeon crawler where the dungeon is a **living record** of your playthroughs. The LLM (Gemini API) acts as the **Chronicle** — at the end of each floor it reads your session data and generates the next floor's theme, enemies, and a new spell tailored to (and against) how you've been playing.

The game runs indefinitely, scaling in difficulty. There are no boss stages — just continuous escalation.

---

## LLM Integration: Gemini API + Function Calling

The game uses **Gemini's function calling** (structured tool use) to enforce a strict JSON schema response. This makes parsing reliable — Gemini must return valid structured data or the call fails cleanly.

### The Input: Session Log

At the end of each floor, the game assembles a Session Log and sends it to Gemini:

```json
{
  "stage_number": 7,
  "combat_style": "aggressive",
  "primary_element": "fire",
  "most_damage_taken_from": "ranged_enemies",
  "last_word": "ember",
  "spells_used": ["Crying Cinder", "Void Shard"]
}
```

- **Combat Style:** Derived from how much the player moved, how close they got to enemies
- **Affinity:** Which element/damage type was used most
- **Trauma:** The enemy type or trap that dealt the most damage this floor
- **The "Last Word":** A single word the player chooses at the end of a floor to "seal" it — flavour + context for the LLM
- **Stage Number:** Tells Gemini how hard to make the next floor

### The Output: Floor Manifest (via function calling)

Gemini returns a single structured `generate_floor` response:

```json
{
  "floor_name": "The Shattered Clockwork",
  "layout_type": "corridor",
  "tileset_id": "clockwork_ruins",
  "palette_override": ["#8B4513", "#C0A080", "#2F2F2F"],
  "environmental_modifier": "time_dilation_field",
  "enemy_spawns": [
    { "enemy_id": "ranged_sentinel", "count": 4, "modifiers": ["armored"] },
    { "enemy_id": "fast_skitter", "count": 6, "modifiers": [] }
  ],
  "new_spell": {
    "name": "Chronos Bolt",
    "flavor": "A bolt that stutters through time, hitting twice.",
    "tags": ["PROJECTILE", "STUTTER_MOTION", "DOUBLE_HIT"],
    "damage": 35,
    "speed": 6.0,
    "cooldown": 1.2
  },
  "corrupted_spells": [
    {
      "spell_name": "Crying Cinder",
      "added_tags": ["SELF_DAMAGE"],
      "removed_tags": ["LIFESTEAL"],
      "new_flavor": "The cinder no longer distinguishes friend from foe."
    }
  ]
}
```

The `layout_type` field is an **enum** — Gemini picks from `["corridor", "arena", "crossroads", "maze"]`. Layouts are premade Unity tilemaps, not generated from scratch.

---

## Enemy System

### Design Approach: Preset Archetypes + LLM Composition

Enemies are **not generated** — they are a fixed library of archetypes with coded behavior. The LLM decides *which* enemies appear, *how many*, and *what modifiers* apply.

### Enemy Archetype Library (8–12 types)

| ID | Description |
|----|-------------|
| `melee_charger` | Rushes directly at player |
| `ranged_sentinel` | Stays at distance, fires projectiles |
| `tank_brute` | High HP, slow, deals knockback |
| `fast_skitter` | Low HP, erratic movement, swarm |
| `exploder` | Chases then detonates on proximity |
| `shield_bearer` | Blocks frontal damage |
| `healer_drone` | Buffs nearby enemies |
| `mimic_shade` | Copies the player's last used spell behavior |

More can be added. Each archetype has defined base stats (HP, damage, speed, behavior state machine).

### Enemy Modifiers (predefined flags)

The LLM applies these to any archetype to make enemies feel "evolved" without new code:

- `armored` — 50% damage reduction
- `berserk` — doubled speed and damage, half HP
- `elemental_immune: fire` — immune to the player's dominant element
- `spectral` — phases through walls briefly
- `regenerating` — slowly heals HP

The engine handles all modifier logic. The LLM just picks which flags to attach based on the player's session log (e.g., if the player uses fire, Gemini will start spawning `elemental_immune: fire` enemies).

---

## Camera & Map Bounds

### Camera Follow
The Main Camera uses `CameraFollow.cs` (on the camera object itself). It follows the player with smooth lerp and clamps so the camera never shows outside the map edge.

- **Standard map size:** `40 × 22` world units (40 × 22 tiles, 1 world unit per tile, 16:9)
- Camera orthographic size = `5` → camera viewport = 17.8 × 10 tiles. Map is ~2.2× wider and ~2.3× taller than the viewport.
- This constant is defined in `CameraFollow.StandardMapSize` and referenced by `MapBoundsMarker`

### MapBoundsMarker
Every scene must have a single **`MapBounds`** empty GameObject with a `MapBoundsMarker` component attached. `CameraFollow` finds it automatically via `FindObjectOfType` at `Start()`.

- Position it at the **centre** of the playable area
- Set its `size` field to match the layout (default: `1280 × 736`)
- It draws a **cyan wire box gizmo** in the Scene view at all times so you can see the boundary while painting tiles
- Falls back to the camera's inspector fields if no marker is present in the scene

### Map Creation Workflow

Scene hierarchy for every floor:

```
Grid  (Unity Grid component, Cell Size = 0.32 × 0.32 for 32 px tiles)
├── Ground    (Tilemap — base floor tiles, rendered below everything)
├── Walls     (Tilemap + TilemapCollider2D + CompositeCollider2D — solid blockers)
├── Details   (Tilemap — decorative overlays, no collider)
└── MapBounds (Empty GameObject + MapBoundsMarker — defines camera clamp region)
```

**Steps to build a new floor:**

1. Create a **Grid** GameObject (`2D Object → Tilemap → Rectangular`) — leave Cell Size at `(1, 1, 0)`
2. Add child Tilemaps: rename to `Ground`, `Walls`, `Details`
   - On `Walls`: add `TilemapCollider2D` → enable **Used By Composite** → add `CompositeCollider2D` (sets Rigidbody2D to Static automatically)
3. Add an empty child GameObject named **`MapBounds`** → attach `MapBoundsMarker`
   - Place it at the centre of the painted area
   - Leave `size` at `(40, 23)` for standard arena layouts; adjust per layout type (see table below)
4. Paint tiles using the **Tile Palette** window
5. Keep all tile painting within the cyan gizmo boundary

**Layout-specific sizes** (defined in Gemini's `layout_type` enum):

All sizes are in **world units** (1 unit = 1 tile, Grid Cell Size = 1).

| layout_type  | Suggested map size  | Notes                          |
|--------------|---------------------|--------------------------------|
| `arena`      | 40 × 22             | Standard, open room (16:9)     |
| `corridor`   | 50 × 16             | Wide and narrow                |
| `crossroads` | 40 × 40             | Symmetric, square              |
| `maze`       | 50 × 32             | Large for complexity           |

---

## Level Generation

### Stage 1 — Fixed Handcrafted Floor

**Stage 1 is always the same hardcoded floor.** It is not Gemini-generated. Its purpose is to:
- Give the player a consistent, tuned introduction
- Serve as the concrete template we build all game systems against (enemies, spells, tilemap loading, session log assembly)
- Guarantee the first Gemini call has real, meaningful session data to react to

**Stage 1 manifest (hardcoded, not from Gemini):**
```json
{
  "floor_name": "The Hollow Entry",
  "layout_type": "arena",
  "tileset_id": "crystal_caves",
  "palette_override": [],
  "environmental_modifier": null,
  "enemy_spawns": [
    { "enemy_id": "melee_charger", "count": 3, "modifiers": [] },
    { "enemy_id": "ranged_sentinel", "count": 2, "modifiers": [] }
  ],
  "new_spell": {
    "name": "Void Shard",
    "flavor": "A raw sliver of nothing, flung outward with intent.",
    "tags": ["PROJECTILE"],
    "damage": 20,
    "speed": 8.0,
    "cooldown": 0.4
  },
  "corrupted_spells": []
}
```

At the end of Stage 1, the session log is assembled and **the first Gemini call is made** to generate Stage 2. Every stage from 2 onwards is Gemini-generated.

### Stages 2+ — Gemini Generated

### Layout
Floors use **premade Unity tilemap templates** selected by the LLM via the `layout_type` enum. No procedural tile-by-tile generation — instead, 4–6 handcrafted room templates per type, randomly selected within the chosen category.

### Tileset Integration (PixelLab Assets)
All 32×32 tilesets created in PixelLab are registered in a `tileset_registry.json`:

```json
{
  "crystal_caves": "Assets/Tilemaps/crystal_caves",
  "clockwork_ruins": "Assets/Tilemaps/clockwork_ruins",
  "flesh_dungeon": "Assets/Tilemaps/flesh_dungeon",
  "frozen_wastes": "Assets/Tilemaps/frozen_wastes"
}
```

This registry is included in the Gemini **system prompt** so the model knows exactly what's available. The `tileset_id` in the Floor Manifest must be a valid key from this registry. The optional `palette_override` (array of hex codes) is applied to tilemap materials at runtime via Unity's shader/material color properties to shift the floor's colour vibe without needing new art.

### Difficulty Scaling
Stage number is always passed in the session log. Difficulty scales via:
- Enemy stat multiplier: `base_stat * (1 + stage * 0.08)` — tunable curve
- Gemini is prompted to increase enemy density and modifier count as stage increases
- Spell corruption rate increases with stage (more tags mutated per floor)

---

## Spell System

### Core Concept: Tag Composition

Spells are **pure data** — no runtime code generation. The engine contains handlers for every possible behavior tag. The LLM creates novel spells by composing new *combinations* of tags, producing emergent behaviors without any new code.

### Behavior Tag Vocabulary (~25–30 tags)

**Movement:**
`PROJECTILE`, `ORBITAL`, `BEAM`, `HOMING`, `WALL_BOUNCE`, `SPIRAL`, `STUTTER_MOTION`, `SPLIT_ON_IMPACT`

**Effect:**
`LIFESTEAL`, `AOE_BURST`, `PIERCE`, `CHAIN`, `SLOW`, `DOUBLE_HIT`, `PULL`, `PUSH`

**Corruption (added by LLM as decay):**
`SELF_DAMAGE`, `ENEMY_HOMING`, `FRIENDLY_FIRE`, `REVERSED_CONTROLS`

### Spell Data Structure

```json
{
  "name": "Crying Cinder",
  "flavor": "A flame that circles the caster, seeking warmth in an enemy's heart.",
  "tags": ["PROJECTILE", "ORBITAL", "LIFESTEAL"],
  "damage": 28,
  "speed": 4.5,
  "cooldown": 0.8,
  "element": "fire",
  "is_merged": false,
  "merged_from": []
}
```

### Spell Generation
Each floor, Gemini generates **one new spell** added to the player's Grimoire. No choice is offered — what the Chronicle gives, you receive. The spell is informed by the player's session log and stage number.

### Spell Corruption (Decay System)
The `corrupted_spells` array in the Floor Manifest can mutate existing spells:
- Adds degrading tags (`SELF_DAMAGE`, `ENEMY_HOMING`)
- Removes beneficial tags (`LIFESTEAL`, `HOMING`)
- Updates the flavor text to reflect the corruption narratively
- This is how the dungeon "fights back" against the player's best weapons

---

## Grimoire: Spell Collection & Loadout

### The Grimoire (Spell Inventory)
The player accumulates spells across floors. The Grimoire holds all owned spells — there is no hard cap enforced in the design, but the UI should cap display at ~8 slots for clarity.

### Active Spell: One at a Time
During a run, the player has **one active spell** at a time as their primary fire. They can open the **Spell Menu** (accessible mid-floor) to switch which spell is currently equipped. This is like weapon-switching in a shooter — tactical, not overwhelming.

There is no secondary fire for now.

### Spell Merging
At **milestone stages** (every 5 floors: stage 5, 10, 15...) the player is offered a **Merge Ritual**:

- Select **2 or 3 spells** from the Grimoire to fuse
- The merged spell fires all component spells **simultaneously** when cast
- All behavior tags from all components are inherited
- The LLM is called to generate a new name and flavor text for the merged spell
- The source spells are **consumed** (removed from Grimoire)
- Merged spells **cannot be merged further** — this prevents runaway power stacking

```json
{
  "name": "The Widow's Eclipse",
  "flavor": "Two lost fires, finally reunited in annihilation.",
  "tags": ["PROJECTILE", "ORBITAL", "LIFESTEAL", "HOMING", "AOE_BURST"],
  "damage": 55,
  "speed": 4.0,
  "cooldown": 1.5,
  "element": "fire",
  "is_merged": true,
  "merged_from": ["Crying Cinder", "Seeking Mote"]
}
```

The merge is a **power trade-off**: you gain a single more powerful spell but lose two individual options (less flexibility, more raw output).

---

## Visual Strategy (PixelLab + Unity)

### Modular Sprite System
- **Enemy bases:** Blank archetype sprites (Humanoid, Quadruped, Eye). Fixed art per archetype.
- **Palette swapping:** Runtime material color replacement driven by `palette_override` from the Floor Manifest. Changes the floor "vibe" without new art assets.
- **Tileset swapping:** Unity loads the tileset assets for the LLM-chosen `tileset_id` at floor start.

### Spell Visuals
Spell visual behavior is driven by its tags — `ORBITAL` triggers the orbital rotation animator, `BEAM` switches to a beam prefab, etc. Art is modular per tag, composed at runtime.

---

## Architecture Summary

```
[Start Game]
      │
      ▼
[Stage 1 — Hardcoded Floor Manifest]   ← no Gemini call
      │
      ▼
[Player runs Stage 1]
      │
      ▼
[End of Floor]
      │
      ▼
[Build Session Log JSON]
      │
      ▼
[Gemini API — function calling with generate_floor schema]   ← first call here
      │
      ▼
[Floor Manifest JSON]
      │
   ┌──┴──────────────────────────────────┐
   │                                     │
[Apply floor theme]              [Add new spell to Grimoire]
[Load tileset + palette]         [Corrupt existing spells]
[Spawn enemies per spawn list]
[Select layout template]
   │
   ▼
[Player runs floor]
[Can switch active spell mid-floor via Spell Menu]
   │
   ▼
[Stage 5/10/15... → Merge Ritual offered]
   │
   ▼
[Repeat]
```
