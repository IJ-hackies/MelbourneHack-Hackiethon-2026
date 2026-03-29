---
name: generate-chambers
description: Generate 8 chamber layouts for a tileset and write chambers.json. Run multiple instances in parallel for different tilesets.
argument-hint: <tileset name e.g. dungeon, acid, flames>
disable-model-invocation: true
---

## What This Skill Does

Generates all 8 chamber layouts for a single tileset and writes the result to `Assets/Chambers/<tileset>/chambers.json`. You ARE the generator — design every layout yourself, don't call any external API.

Valid tilesets: `acid`, `bubblegum`, `dungeon`, `flames`, `forest`, `frozen`, `honey`, `ocean`, `rocky`, `techy`

---

## Step 1 — Resolve Tileset

If `$ARGUMENTS` is provided, use it as the tileset name (lowercase, trimmed). Otherwise ask:

> "Which tileset? (acid, bubblegum, dungeon, flames, forest, frozen, honey, ocean, rocky, techy)"

Store as `TILESET`. Reject anything not in the list above.

---

## Step 2 — Load Tile Data

Read `generate_chambers.py` from the project root. Extract from it:
- `TILE_CLASSIFICATIONS[TILESET]` — the `primary_floor`, `primary_wall`, `alt_floor`, `accent_wall`, `detail_tiles` indices
- `HAZARD_THEME[TILESET]` — whether O (void) tiles are available and what they represent
- The 8 `ARCHETYPES` entries (key, tags, use_o, brief)

---

## Step 3 — Generate 8 Chamber Layouts

For each of the 8 archetypes, design a 20x20 tile grid. You must produce the layout yourself as an array of 20 strings, each exactly 20 characters.

### Tile Vocabulary

Every tileset supports ALL of these (the indices vary but the letters are universal):

| Char | Layer | Walkable? | Role |
|------|-------|-----------|------|
| `F` | Ground | Yes | Primary floor — dominant tile |
| `W` | Walls | **No** | Primary wall — blocks movement |
| `A` | Ground | Yes | Alternate floor — visually different, use for patterns |
| `B` | Walls | **No** | Accent wall — blocks movement, different look than W |
| `D` | Details | Yes | Detail overlay 1 — decorative accent |
| `E` | Details | Yes | Detail overlay 2 — different decorative accent |
| `G` | Details | Yes | Detail overlay 3 — rare focal accent |
| `H` | Details | Yes | Detail overlay 4 — threshold/transition accent |
| `O` | None | **No** | Void/hazard — only in `hazard_field` archetype if tileset supports it |

### Hard Constraints (every layout MUST satisfy ALL of these)

1. **Grid size**: exactly 20 rows, each exactly 20 characters.

2. **Four entry openings** — these 8 cells MUST be `F`:
   - Row 0, cols 9-10 (top entry)
   - Row 19, cols 9-10 (bottom entry)
   - Col 0, rows 9-10 (left entry)
   - Col 19, rows 9-10 (right entry)
   - So rows 0 and 19 look like: `WWWWWWWWWFFWWWWWWWWWw`
   - Rows 9 and 10 start with `F` and end with `F`
   - All other border rows start and end with `W`

3. **Full connectivity**: every walkable tile (F, A, D, E, G, H) must be reachable from every entry by 4-directional movement through walkable tiles. No isolated pockets.

4. **Minimum corridor width**: all passable paths must be >= 2 tiles wide. No walkable cell may be squeezed between blocking tiles (W, B, O) on both opposite sides (left+right or top+bottom).

5. **Wall density**: W + B + O tiles must not exceed 40% of the grid (<= 160 of 400 cells).

6. **Tile variety minimums**:
   - A tiles: >= 15
   - D tiles: >= 6
   - E tiles: >= 4
   - G tiles: >= 2
   - H tiles: >= 2

7. **O tiles**: only use in the `hazard_field` archetype, and only if `HAZARD_THEME[TILESET]` is not None.

### The 8 Archetypes to Generate

Generate them in this order, using these exact IDs:

1. **`open_arena`** (tags: open, ranged_favored, symmetric) — Wide open room, almost no interior walls. At most 4 small 2x2 pillar obstacles. Spacious.

2. **`center_block`** (tags: flanking, cover_heavy, symmetric) — Solid 6x6 to 8x8 wall block in center, ring corridor around it (3-5 tiles wide).

3. **`four_pillars`** (tags: scattered_pillars, symmetric, melee_favored) — Four 3x3 to 4x4 wall blocks, one per quadrant. Corridors >= 3 tiles wide between pillars.

4. **`scattered_cover`** (tags: cover_heavy, low_visibility) — 5-8 irregular wall blocks of varying sizes, asymmetric, no two adjacent.

5. **`hazard_field`** (tags: chokepoint, ranged_favored) — O tiles fill interior, leaving >= 2-wide safe paths connecting all 4 entries. Central junction. Skip O and use W instead if tileset has no hazard theme.

6. **`diagonal_split`** (tags: symmetric, flanking, chokepoint) — Two diagonal wall formations from opposite corners toward center, 2-3 tiles thick.

7. **`grid_pillars`** (tags: scattered_pillars, low_visibility, cover_heavy) — Regular grid of 2x2 wall pillars in 3x3 or 3x4 pattern, 2-wide corridors between.

8. **`divided_rooms`** (tags: chokepoint, melee_favored, low_visibility) — Room divided into 2-3 sub-areas by thick interior walls, connected by 3-4 tile wide doorways.

### Design Quality Guidelines

- **Use ALL tile types** — layouts with only F and W are garbage. Use A for floor patterns (borders, diamonds, cross-paths), B for accent walls on pillars/doorframes, D/E/G/H for scattered decorative detail.
- **Make each chamber visually distinct** — don't just change wall placement, vary the floor patterns too.
- **No dead ends or traps** — every walkable area must have multiple ways in/out.
- **Think about gameplay** — rooms should support the combat style indicated by their tags.

---

## Step 4 — Validate Each Layout

After generating each layout, run the validator from `generate_chambers.py` (project root):

```bash
python -c "
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
from generate_chambers import validate_layout
layout = [
    'ROW0HERE',
    'ROW1HERE',
    ...all 20 rows...
]
ok, msg = validate_layout(layout, 'TILESET', USE_O_BOOL)
print(f'{\"PASS\" if ok else \"FAIL\"}: {msg}')
"
```

The validator checks: grid size, entry openings, border walls, BFS connectivity
(no isolated pockets), corridor width (no 1-wide squeezes), wall density (<=40%),
tile variety minimums (A>=15, D>=6, E>=4, G>=2, H>=2), and O-tile restrictions.

If a layout fails validation, fix it and re-validate. Do not proceed to writing the file until ALL 8 layouts pass.

---

## Step 5 — Write chambers.json

Create the directory if needed and write `Assets/Chambers/TILESET/chambers.json` with this exact structure:

```json
{
  "tileset_id": "TILESET",
  "tile_classification": {
    "primary_floor": N,
    "primary_wall": N,
    "alt_floor": N,
    "accent_wall": N,
    "detail_tiles": [N, N, N, N]
  },
  "chambers": [
    {
      "id": "TILESET_ARCHETYPE_01",
      "description": "Short description of the room's gameplay character",
      "tags": ["tag1", "tag2"],
      "layout": ["20 strings of 20 chars each"]
    },
    ... 8 total
  ]
}
```

The `tile_classification` values come from `TILE_CLASSIFICATIONS[TILESET]` in `generate_chambers.py`.

Chamber IDs follow the pattern: `{tileset}_{archetype_key}_{index_padded}` e.g. `dungeon_open_arena_01`, `dungeon_center_block_02`, etc.

---

## Step 6 — Final Validation

Run the full validator on all 8 chambers (replace TILESET with the actual name):

```bash
python -c "
import sys, io, json
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
from generate_chambers import validate_layout, HAZARD_THEME
tileset = 'TILESET'
with open(f'Assets/Chambers/{tileset}/chambers.json') as f:
    data = json.load(f)
for ch in data['chambers']:
    use_o = 'hazard' in ch['id'] and HAZARD_THEME.get(tileset) is not None
    ok, msg = validate_layout(ch['layout'], tileset, use_o)
    print(f'{ch[\"id\"]}: {\"PASS\" if ok else \"FAIL\"} -- {msg}')
"
```

Report the results. If any fail, fix and re-write.

---

## Notes

- You are the layout designer. Do NOT call any external API — generate the 20x20 grids yourself.
- Each row must be EXACTLY 20 characters. Count carefully.
- The most common mistake is getting row length wrong. Count every row before writing.
- Always validate with the Python script — it catches connectivity, corridor width, density, and tile variety issues that are easy to miss.
