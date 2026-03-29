#!/usr/bin/env python3
"""
Procedural chamber generator for Everchanging Grimoire.

Generates 8 archetype layouts (shared across all tilesets) and writes
Assets/Chambers/<tileset>/chambers.json for every tileset.

Usage:
    python generate_chambers.py            # generate all 10 tilesets
    python generate_chambers.py dungeon    # generate one tileset

Then in Unity: Tools > Generate Chamber Prefabs
"""

import json, os, sys, random
from collections import deque

# ── Tile classifications per tileset ──────────────────────────────────────────
# Indices into a 4×4 spritesheet (0-15).  Layout:
#   0  1  2  3
#   4  5  6  7
#   8  9 10 11
#  12 13 14 15

TILE_CLASSIFICATIONS = {
    "dungeon":    {"primary_floor": 5,  "primary_wall": 0,  "alt_floor": 6,  "accent_wall": 3,  "detail_tiles": [9, 10, 13, 14]},
    "acid":       {"primary_floor": 5,  "primary_wall": 0,  "alt_floor": 6,  "accent_wall": 3,  "detail_tiles": [9, 10, 13, 14]},
    "bubblegum":  {"primary_floor": 5,  "primary_wall": 0,  "alt_floor": 6,  "accent_wall": 3,  "detail_tiles": [9, 10, 13, 14]},
    "flames":     {"primary_floor": 5,  "primary_wall": 0,  "alt_floor": 6,  "accent_wall": 3,  "detail_tiles": [9, 10, 13, 14]},
    "forest":     {"primary_floor": 5,  "primary_wall": 0,  "alt_floor": 6,  "accent_wall": 3,  "detail_tiles": [9, 10, 13, 14]},
    "frozen":     {"primary_floor": 5,  "primary_wall": 0,  "alt_floor": 6,  "accent_wall": 3,  "detail_tiles": [9, 10, 13, 14]},
    "honey":      {"primary_floor": 5,  "primary_wall": 0,  "alt_floor": 6,  "accent_wall": 3,  "detail_tiles": [9, 10, 13, 14]},
    "ocean":      {"primary_floor": 5,  "primary_wall": 0,  "alt_floor": 6,  "accent_wall": 3,  "detail_tiles": [9, 10, 13, 14]},
    "rocky":      {"primary_floor": 5,  "primary_wall": 0,  "alt_floor": 6,  "accent_wall": 3,  "detail_tiles": [9, 10, 13, 14]},
    "techy":      {"primary_floor": 5,  "primary_wall": 0,  "alt_floor": 6,  "accent_wall": 3,  "detail_tiles": [9, 10, 13, 14]},
}

# Which tilesets support O (void/hazard) tiles and what they represent
HAZARD_THEME = {
    "dungeon":   "bottomless pits",
    "acid":      "acid pools",
    "bubblegum": None,
    "flames":    "lava",
    "forest":    None,
    "frozen":    "thin ice chasms",
    "honey":     None,
    "ocean":     "deep water",
    "rocky":     "chasms",
    "techy":     "energy fields",
}

# 8 archetype definitions
ARCHETYPES = [
    {"key": "open_arena",      "num": "01", "tags": ["open", "ranged_favored", "symmetric"],
     "desc": "Wide open arena with minimal obstacles. Four small 2x2 pillars provide sparse cover for ranged combat and kiting."},
    {"key": "center_block",    "num": "02", "tags": ["flanking", "cover_heavy", "symmetric"],
     "desc": "Solid wall block in the center with a ring corridor around it. Forces flanking movement."},
    {"key": "four_pillars",    "num": "03", "tags": ["scattered_pillars", "symmetric", "melee_favored"],
     "desc": "Four wall blocks, one per quadrant, with wide corridors between. Good for melee ambushes."},
    {"key": "scattered_cover", "num": "04", "tags": ["cover_heavy", "low_visibility"],
     "desc": "Asymmetric room with scattered irregular wall blocks providing varied cover and broken sightlines."},
    {"key": "hazard_field",    "num": "05", "tags": ["chokepoint", "ranged_favored"],
     "desc": "Hazard zones fill the interior with safe paths connecting all entries through a central junction."},
    {"key": "diagonal_split",  "num": "06", "tags": ["symmetric", "flanking", "chokepoint"],
     "desc": "Two diagonal wall formations from opposite corners create split zones with flanking routes."},
    {"key": "grid_pillars",    "num": "07", "tags": ["scattered_pillars", "low_visibility", "cover_heavy"],
     "desc": "Regular grid of 2x2 wall pillars with corridors between. Breaks sightlines in all directions."},
    {"key": "divided_rooms",   "num": "08", "tags": ["chokepoint", "melee_favored", "low_visibility", "divided_rooms"],
     "desc": "Room divided into sub-areas by interior walls, connected by wide doorways. Favors close-range combat."},
]

SIZE = 20
WALKABLE = set("FADEGH")
BLOCKING = set("WBO")


# ── Validation ────────────────────────────────────────────────────────────────

def validate_layout(layout, tileset="dungeon", use_o=False):
    """Validate a 20x20 chamber layout. Returns (ok, message)."""
    # 1. Grid size
    if len(layout) != 20:
        return False, f"Expected 20 rows, got {len(layout)}"
    for i, row in enumerate(layout):
        if len(row) != 20:
            return False, f"Row {i} has {len(row)} chars, expected 20"

    # 2. Entry openings -- must be F
    entries = [(0, 9), (0, 10), (19, 9), (19, 10)]  # top/bottom
    for r in [9, 10]:
        entries.append((r, 0))   # left
        entries.append((r, 19))  # right
    for r, c in entries:
        if layout[r][c] != 'F':
            return False, f"Entry at ({r},{c}) must be F, got '{layout[r][c]}'"

    # 3. Border walls -- non-entry border cells must be W
    for c in range(20):
        if (0, c) not in [(0, 9), (0, 10)]:
            if layout[0][c] != 'W':
                return False, f"Border row 0 col {c} must be W, got '{layout[0][c]}'"
        if (19, c) not in [(19, 9), (19, 10)]:
            if layout[19][c] != 'W':
                return False, f"Border row 19 col {c} must be W, got '{layout[19][c]}'"
    for r in range(1, 19):
        if (r, 0) not in [(9, 0), (10, 0)]:
            if layout[r][0] != 'W':
                return False, f"Border col 0 row {r} must be W, got '{layout[r][0]}'"
        if (r, 19) not in [(9, 19), (10, 19)]:
            if layout[r][19] != 'W':
                return False, f"Border col 19 row {r} must be W, got '{layout[r][19]}'"

    # 4. Valid characters
    valid = set("FWABDEGH") | ({"O"} if use_o else set())
    for r in range(20):
        for c in range(20):
            if layout[r][c] not in valid:
                return False, f"Invalid char '{layout[r][c]}' at ({r},{c})"

    # 5. O tiles only in hazard_field
    if not use_o:
        for r in range(20):
            for c in range(20):
                if layout[r][c] == 'O':
                    return False, f"O tile at ({r},{c}) but use_o=False"

    # 6. BFS connectivity -- all walkable tiles reachable from entry (0,9)
    walkable_cells = set()
    for r in range(20):
        for c in range(20):
            if layout[r][c] in WALKABLE:
                walkable_cells.add((r, c))

    if not walkable_cells:
        return False, "No walkable tiles"

    start = (0, 9)  # top entry, guaranteed F
    visited = set()
    queue = deque([start])
    visited.add(start)
    while queue:
        cr, cc = queue.popleft()
        for dr, dc in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
            nr, nc = cr + dr, cc + dc
            if 0 <= nr < 20 and 0 <= nc < 20 and (nr, nc) not in visited:
                if layout[nr][nc] in WALKABLE:
                    visited.add((nr, nc))
                    queue.append((nr, nc))

    if visited != walkable_cells:
        isolated = walkable_cells - visited
        return False, f"Connectivity fail: {len(isolated)} isolated walkable tiles"

    # 7. Corridor width -- no 1-wide squeezes
    for r in range(1, 19):
        for c in range(1, 19):
            if layout[r][c] in WALKABLE:
                # Check horizontal squeeze (blocked left AND right)
                if layout[r][c-1] in BLOCKING and layout[r][c+1] in BLOCKING:
                    return False, f"1-wide horizontal squeeze at ({r},{c})"
                # Check vertical squeeze (blocked above AND below)
                if layout[r-1][c] in BLOCKING and layout[r+1][c] in BLOCKING:
                    return False, f"1-wide vertical squeeze at ({r},{c})"

    # 8. Wall density <= 40%
    blocking_count = sum(1 for r in range(20) for c in range(20) if layout[r][c] in BLOCKING)
    if blocking_count > 160:
        return False, f"Wall density {blocking_count}/400 exceeds 40% (160 max)"

    # 9. Tile variety minimums
    counts = {}
    for r in range(20):
        for c in range(20):
            ch = layout[r][c]
            counts[ch] = counts.get(ch, 0) + 1

    mins = {"A": 15, "D": 6, "E": 4, "G": 2, "H": 2}
    for tile, minimum in mins.items():
        actual = counts.get(tile, 0)
        if actual < minimum:
            return False, f"Tile '{tile}' count {actual} < minimum {minimum}"

    return True, "OK"


# ── Layout generators ─────────────────────────────────────────────────────────

def _make_empty():
    """Start with a 20x20 grid: W border with F entry openings, F interior."""
    grid = [['F'] * 20 for _ in range(20)]
    # Top/bottom border
    for c in range(20):
        grid[0][c] = 'W'
        grid[19][c] = 'W'
    # Left/right border
    for r in range(20):
        grid[r][0] = 'W'
        grid[r][19] = 'W'
    # Carve entry openings
    for c in [9, 10]:
        grid[0][c] = 'F'
        grid[19][c] = 'F'
    for r in [9, 10]:
        grid[r][0] = 'F'
        grid[r][19] = 'F'
    return grid


def _place_block(grid, r1, c1, r2, c2, ch='W'):
    """Place a rectangular block, respecting borders and entries."""
    entries = {(0, 9), (0, 10), (19, 9), (19, 10),
               (9, 0), (10, 0), (9, 19), (10, 19)}
    for r in range(r1, r2):
        for c in range(c1, c2):
            if 0 <= r < 20 and 0 <= c < 20 and (r, c) not in entries:
                grid[r][c] = ch


def _sprinkle_details(grid, rng):
    """Add A, D, E, G, H tiles to floor areas for variety."""
    interior = []
    for r in range(1, 19):
        for c in range(1, 19):
            if grid[r][c] == 'F':
                interior.append((r, c))

    rng.shuffle(interior)
    # A tiles: create floor patterns (borders, paths)
    a_count = 0
    # Place A tiles in a cross pattern through the center
    for r in range(1, 19):
        for c in range(1, 19):
            if grid[r][c] == 'F':
                # Create a ring/cross pattern with A tiles
                dr = abs(r - 9.5)
                dc = abs(c - 9.5)
                if (4 <= dr <= 5 and dc <= 6) or (4 <= dc <= 5 and dr <= 6):
                    grid[r][c] = 'A'
                    a_count += 1

    # If not enough A tiles, add more from remaining F tiles
    remaining_f = [(r, c) for r in range(1, 19) for c in range(1, 19) if grid[r][c] == 'F']
    rng.shuffle(remaining_f)
    while a_count < 18 and remaining_f:
        r, c = remaining_f.pop()
        grid[r][c] = 'A'
        a_count += 1

    # D, E, G, H scattered on remaining F tiles
    remaining_f = [(r, c) for r in range(1, 19) for c in range(1, 19) if grid[r][c] == 'F']
    rng.shuffle(remaining_f)
    detail_needs = [('D', 8), ('E', 6), ('H', 4), ('G', 3)]
    for ch, count in detail_needs:
        placed = 0
        while placed < count and remaining_f:
            r, c = remaining_f.pop()
            grid[r][c] = ch
            placed += 1


def _sprinkle_accent_walls(grid, rng, fraction=0.3):
    """Convert some W blocks (non-border) to B for visual variety."""
    interior_walls = []
    border = set()
    for c in range(20):
        border.add((0, c))
        border.add((19, c))
    for r in range(20):
        border.add((r, 0))
        border.add((r, 19))

    for r in range(1, 19):
        for c in range(1, 19):
            if grid[r][c] == 'W' and (r, c) not in border:
                interior_walls.append((r, c))

    rng.shuffle(interior_walls)
    convert = int(len(interior_walls) * fraction)
    for i in range(min(convert, len(interior_walls))):
        r, c = interior_walls[i]
        grid[r][c] = 'B'


def _to_strings(grid):
    return ["".join(row) for row in grid]


# ── Archetype generators ─────────────────────────────────────────────────────

def gen_open_arena(rng):
    """Wide open room with 4 small 2x2 pillars."""
    grid = _make_empty()
    # Four 2x2 pillars in quadrants
    pillars = [(4, 4), (4, 14), (14, 4), (14, 14)]
    for pr, pc in pillars:
        _place_block(grid, pr, pc, pr + 2, pc + 2, 'B')
    _sprinkle_details(grid, rng)
    return _to_strings(grid)


def gen_center_block(rng):
    """Solid 7x7 wall block in center, ring corridor around it."""
    grid = _make_empty()
    _place_block(grid, 6, 6, 13, 13, 'W')
    _sprinkle_accent_walls(grid, rng, 0.4)
    _sprinkle_details(grid, rng)
    return _to_strings(grid)


def gen_four_pillars(rng):
    """Four 3x3 wall blocks, one per quadrant."""
    grid = _make_empty()
    positions = [(3, 3), (3, 14), (14, 3), (14, 14)]
    for pr, pc in positions:
        _place_block(grid, pr, pc, pr + 3, pc + 3, 'W')
    _sprinkle_accent_walls(grid, rng, 0.5)
    _sprinkle_details(grid, rng)
    return _to_strings(grid)


def gen_scattered_cover(rng):
    """5-7 irregular wall blocks, asymmetric."""
    grid = _make_empty()
    blocks = [
        (2, 3, 4, 6),    # top-left blob
        (3, 13, 5, 16),  # top-right blob
        (7, 7, 9, 10),   # center-left
        (11, 11, 13, 14), # center-right
        (14, 2, 17, 4),  # bottom-left
        (15, 14, 17, 17), # bottom-right
    ]
    for r1, c1, r2, c2 in blocks:
        _place_block(grid, r1, c1, r2, c2, 'W')
    _sprinkle_accent_walls(grid, rng, 0.4)
    _sprinkle_details(grid, rng)
    return _to_strings(grid)


def gen_hazard_field(rng, use_o=True):
    """O tiles fill corners, safe paths connect entries through center.
    Border W = ~68 tiles, so interior hazard must stay under ~90 to keep
    total blocking <= 160 (40%).  We use 9-wide cross paths (rows 5-13,
    cols 5-13) leaving hazard only in the four corners.
    """
    grid = _make_empty()
    hazard_ch = 'O' if use_o else 'W'

    # Fill interior with hazard
    for r in range(1, 19):
        for c in range(1, 19):
            grid[r][c] = hazard_ch

    # Carve 9-wide cross paths connecting entries
    # Horizontal band (rows 5-13)
    for r in range(5, 14):
        for c in range(1, 19):
            grid[r][c] = 'F'
    # Vertical band (cols 5-13)
    for r in range(1, 19):
        for c in range(5, 14):
            grid[r][c] = 'F'

    # Re-set entries
    for c in [9, 10]:
        grid[0][c] = 'F'
        grid[19][c] = 'F'
    for r in [9, 10]:
        grid[r][0] = 'F'
        grid[r][19] = 'F'

    # Add H tiles along hazard borders (before detail sprinkle so they count)
    for r in range(1, 19):
        for c in range(1, 19):
            if grid[r][c] == 'F':
                for dr, dc in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
                    nr, nc = r + dr, c + dc
                    if 0 <= nr < 20 and 0 <= nc < 20 and grid[nr][nc] == hazard_ch:
                        grid[r][c] = 'H'
                        break

    # Re-ensure entries are F (H pass may have overwritten them)
    for c in [9, 10]:
        grid[0][c] = 'F'
        grid[19][c] = 'F'
    for r in [9, 10]:
        grid[r][0] = 'F'
        grid[r][19] = 'F'

    _sprinkle_details(grid, rng)

    # Final entry safety
    for c in [9, 10]:
        grid[0][c] = 'F'
        grid[19][c] = 'F'
    for r in [9, 10]:
        grid[r][0] = 'F'
        grid[r][19] = 'F'

    return _to_strings(grid)


def gen_diagonal_split(rng):
    """Two diagonal wall formations from opposite corners, 2 tiles thick."""
    grid = _make_empty()
    # Diagonal from top-left toward center (2 tiles thick, stay away from borders)
    for i in range(3, 9):
        for t in range(2):
            r, c = i, i + t
            if 2 <= r <= 17 and 2 <= c <= 17:
                grid[r][c] = 'W'
    # Diagonal from bottom-right toward center (2 tiles thick)
    for i in range(3, 9):
        for t in range(2):
            r, c = 19 - i, 19 - i - t
            if 2 <= r <= 17 and 2 <= c <= 17:
                grid[r][c] = 'W'
    _sprinkle_accent_walls(grid, rng, 0.4)
    _sprinkle_details(grid, rng)
    return _to_strings(grid)


def gen_grid_pillars(rng):
    """3x3 grid of 2x2 wall pillars with corridors between."""
    grid = _make_empty()
    # Place 9 pillars in a 3x3 pattern
    starts = [4, 9, 14]  # row/col starts for the 3x3 grid
    for pr in starts:
        for pc in starts:
            _place_block(grid, pr, pc, pr + 2, pc + 2, 'W')
    _sprinkle_accent_walls(grid, rng, 0.5)
    _sprinkle_details(grid, rng)
    return _to_strings(grid)


def gen_divided_rooms(rng):
    """Room divided into 3 sub-areas by interior walls with doorways."""
    grid = _make_empty()
    # Vertical wall dividers at columns 6-7 and 12-13
    for r in range(1, 19):
        grid[r][6] = 'W'
        grid[r][7] = 'W'
        grid[r][12] = 'W'
        grid[r][13] = 'W'

    # Carve 4-tile wide doorways in each wall
    # Left wall (cols 6-7): doorway at rows 8-11
    for r in range(7, 13):
        grid[r][6] = 'F'
        grid[r][7] = 'F'
    # Right wall (cols 12-13): doorway at rows 8-11
    for r in range(7, 13):
        grid[r][12] = 'F'
        grid[r][13] = 'F'

    # Re-set entries (left/right entries at rows 9-10)
    for r in [9, 10]:
        grid[r][0] = 'F'
        grid[r][19] = 'F'

    _sprinkle_accent_walls(grid, rng, 0.3)
    _sprinkle_details(grid, rng)
    return _to_strings(grid)


GENERATORS = {
    "open_arena":      lambda rng: gen_open_arena(rng),
    "center_block":    lambda rng: gen_center_block(rng),
    "four_pillars":    lambda rng: gen_four_pillars(rng),
    "scattered_cover": lambda rng: gen_scattered_cover(rng),
    "hazard_field":    lambda rng: gen_hazard_field(rng, use_o=True),
    "diagonal_split":  lambda rng: gen_diagonal_split(rng),
    "grid_pillars":    lambda rng: gen_grid_pillars(rng),
    "divided_rooms":   lambda rng: gen_divided_rooms(rng),
}


# ── Main generation logic ────────────────────────────────────────────────────

def generate_tileset(tileset, seed=42):
    """Generate chambers.json for a single tileset."""
    rng = random.Random(seed)
    tc = TILE_CLASSIFICATIONS[tileset]
    has_hazard = HAZARD_THEME.get(tileset) is not None

    chambers = []
    for arch in ARCHETYPES:
        key = arch["key"]
        chamber_id = f"{tileset}_{key}_{arch['num']}"

        # Special handling for hazard_field
        if key == "hazard_field":
            layout = gen_hazard_field(rng, use_o=has_hazard)
        else:
            layout = GENERATORS[key](rng)

        # Validate
        use_o = (key == "hazard_field" and has_hazard)
        ok, msg = validate_layout(layout, tileset, use_o)
        if not ok:
            print(f"  WARNING: {chamber_id} failed validation: {msg}")
            print(f"  Attempting fix...")
            layout = _fix_layout(layout, use_o, rng)
            ok, msg = validate_layout(layout, tileset, use_o)
            if not ok:
                print(f"  STILL FAILING: {chamber_id}: {msg}")
            else:
                print(f"  Fixed: {chamber_id}")

        chambers.append({
            "id": chamber_id,
            "description": arch["desc"],
            "tags": arch["tags"],
            "layout": layout,
        })

    manifest = {
        "tileset_id": tileset,
        "tile_classification": tc,
        "chambers": chambers,
    }
    return manifest


def _fix_layout(layout, use_o, rng):
    """Attempt to fix common validation issues."""
    grid = [list(row) for row in layout]

    # Fix entries
    for c in [9, 10]:
        grid[0][c] = 'F'
        grid[19][c] = 'F'
    for r in [9, 10]:
        grid[r][0] = 'F'
        grid[r][19] = 'F'

    # Fix borders
    for c in range(20):
        if c not in [9, 10]:
            grid[0][c] = 'W'
            grid[19][c] = 'W'
    for r in range(1, 19):
        if r not in [9, 10]:
            grid[r][0] = 'W'
            grid[r][19] = 'W'

    # Remove O tiles if not allowed
    if not use_o:
        for r in range(20):
            for c in range(20):
                if grid[r][c] == 'O':
                    grid[r][c] = 'F'

    # Fix 1-wide squeezes (only modify interior cells, never borders)
    border = set()
    for c in range(20):
        border.add((0, c)); border.add((19, c))
    for r in range(20):
        border.add((r, 0)); border.add((r, 19))

    changed = True
    while changed:
        changed = False
        for r in range(1, 19):
            for c in range(1, 19):
                if grid[r][c] in WALKABLE:
                    if grid[r][c-1] in BLOCKING and grid[r][c+1] in BLOCKING:
                        if (r, c+1) not in border:
                            grid[r][c+1] = 'F'
                            changed = True
                    if grid[r-1][c] in BLOCKING and grid[r+1][c] in BLOCKING:
                        if (r+1, c) not in border:
                            grid[r+1][c] = 'F'
                            changed = True

    # Fix connectivity via BFS -- carve paths to isolated regions
    walkable_cells = {(r, c) for r in range(20) for c in range(20) if grid[r][c] in WALKABLE}
    if walkable_cells:
        start = (0, 9)
        visited = set()
        queue = deque([start])
        visited.add(start)
        while queue:
            cr, cc = queue.popleft()
            for dr, dc in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
                nr, nc = cr + dr, cc + dc
                if 0 <= nr < 20 and 0 <= nc < 20 and (nr, nc) not in visited and grid[nr][nc] in WALKABLE:
                    visited.add((nr, nc))
                    queue.append((nr, nc))

        isolated = walkable_cells - visited
        # Carve paths from isolated cells toward visited cells
        for ir, ic in isolated:
            if grid[ir][ic] not in WALKABLE:
                continue
            # BFS from isolated cell, carving through walls toward visited
            carve_q = deque([(ir, ic, [])])
            carve_visited = {(ir, ic)}
            while carve_q:
                cr, cc, path = carve_q.popleft()
                if (cr, cc) in visited:
                    for pr, pc in path:
                        if 1 <= pr < 19 and 1 <= pc < 19:
                            grid[pr][pc] = 'F'
                    break
                for dr, dc in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
                    nr, nc = cr + dr, cc + dc
                    if 1 <= nr < 19 and 1 <= nc < 19 and (nr, nc) not in carve_visited:
                        carve_visited.add((nr, nc))
                        carve_q.append((nr, nc, path + [(nr, nc)]))

    # Ensure tile variety -- re-sprinkle if needed
    _ensure_variety(grid, rng)

    return ["".join(row) for row in grid]


def _ensure_variety(grid, rng):
    """Ensure minimum tile variety counts are met."""
    counts = {}
    for r in range(20):
        for c in range(20):
            ch = grid[r][c]
            counts[ch] = counts.get(ch, 0) + 1

    mins = {"A": 15, "D": 6, "E": 4, "G": 2, "H": 2}
    entries = {(0, 9), (0, 10), (19, 9), (19, 10),
               (9, 0), (10, 0), (9, 19), (10, 19)}

    for tile, minimum in mins.items():
        actual = counts.get(tile, 0)
        if actual < minimum:
            needed = minimum - actual
            # Find F tiles to convert (not entries, not border)
            candidates = []
            for r in range(1, 19):
                for c in range(1, 19):
                    if grid[r][c] == 'F' and (r, c) not in entries:
                        candidates.append((r, c))
            rng.shuffle(candidates)
            for i in range(min(needed, len(candidates))):
                r, c = candidates[i]
                grid[r][c] = tile
                counts['F'] = counts.get('F', 0) - 1
                counts[tile] = counts.get(tile, 0) + 1


def write_tileset(tileset, out_dir="Assets/Chambers", seed=42):
    """Generate and write chambers.json for a tileset."""
    manifest = generate_tileset(tileset, seed)

    tileset_dir = os.path.join(out_dir, tileset)
    os.makedirs(tileset_dir, exist_ok=True)

    path = os.path.join(tileset_dir, "chambers.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)

    # Validate all
    has_hazard = HAZARD_THEME.get(tileset) is not None
    all_ok = True
    for ch in manifest["chambers"]:
        use_o = "hazard" in ch["id"] and has_hazard
        ok, msg = validate_layout(ch["layout"], tileset, use_o)
        status = "PASS" if ok else "FAIL"
        if not ok:
            all_ok = False
        print(f"  {ch['id']}: {status} -- {msg}")

    return path, all_ok


def stamp_template(template_path, out_dir="Assets/Chambers"):
    """Read a template JSON (from chamber-painter.html) and stamp it across all tilesets."""
    with open(template_path, "r", encoding="utf-8") as f:
        template = json.load(f)

    template_chambers = template["chambers"]
    if len(template_chambers) != 8:
        print(f"ERROR: Template has {len(template_chambers)} chambers, expected 8.")
        sys.exit(1)

    all_ok = True
    for tileset in TILE_CLASSIFICATIONS:
        tc = TILE_CLASSIFICATIONS[tileset]
        has_hazard = HAZARD_THEME.get(tileset) is not None

        chambers = []
        for ch in template_chambers:
            key = ch["key"]
            num = ch["num"]
            chamber_id = f"{tileset}_{key}_{num}"
            use_o = key == "hazard_field" and has_hazard
            layout = ch["layout"]

            # If tileset has no hazard theme, replace O with W
            if not has_hazard and any('O' in row for row in layout):
                layout = [row.replace('O', 'W') for row in layout]

            ok, msg = validate_layout(layout, tileset, use_o)
            status = "PASS" if ok else "FAIL"
            if not ok:
                all_ok = False
            print(f"  {chamber_id}: {status} -- {msg}")

            chambers.append({
                "id": chamber_id,
                "description": ch.get("description", ""),
                "tags": ch.get("tags", []),
                "layout": layout,
            })

        manifest = {
            "tileset_id": tileset,
            "tile_classification": tc,
            "chambers": chambers,
        }

        tileset_dir = os.path.join(out_dir, tileset)
        os.makedirs(tileset_dir, exist_ok=True)
        path = os.path.join(tileset_dir, "chambers.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=2, ensure_ascii=False)
        print(f"  -> Wrote {path}\n")

    return all_ok


def main():
    # Mode 1: stamp from template
    #   python generate_chambers.py --template chamber-template.json
    # Mode 2: procedural generation
    #   python generate_chambers.py [tileset]

    if len(sys.argv) > 1 and sys.argv[1] == "--template":
        if len(sys.argv) < 3:
            print("Usage: python generate_chambers.py --template <path-to-template.json>")
            sys.exit(1)
        template_path = sys.argv[2]
        print(f"Stamping template '{template_path}' across all tilesets...\n")
        ok = stamp_template(template_path)
        if ok:
            print("All chambers stamped and validated successfully.")
            print("Next: open Unity and run Tools > Generate Chamber Prefabs")
        else:
            print("Some chambers have validation errors. Check output above.")
        return

    tilesets = list(TILE_CLASSIFICATIONS.keys())

    if len(sys.argv) > 1:
        requested = sys.argv[1].lower()
        if requested not in TILE_CLASSIFICATIONS:
            print(f"Unknown tileset '{requested}'. Valid: {', '.join(tilesets)}")
            sys.exit(1)
        tilesets = [requested]

    print(f"Generating chambers for {len(tilesets)} tileset(s)...\n")

    all_ok = True
    for tileset in tilesets:
        print(f"[{tileset}]")
        path, ok = write_tileset(tileset, seed=42)
        if ok:
            print(f"  -> Wrote {path}\n")
        else:
            print(f"  -> Wrote {path} (WITH VALIDATION ERRORS)\n")
            all_ok = False

    if all_ok:
        print("All chambers generated and validated successfully.")
        print("Next: open Unity and run Tools > Generate Chamber Prefabs")
    else:
        print("Some chambers have validation errors. Check output above.")


if __name__ == "__main__":
    main()
