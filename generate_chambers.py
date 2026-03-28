#!/usr/bin/env python3
"""
generate_chambers.py

Sends each tileset spritesheet to Gemini and generates 8 chamber layouts
(20×20 tiles each) with metadata, saved to Assets/Chambers/<tileset>/chambers.json

Usage:
    python generate_chambers.py                    # all 10 tilesets
    python generate_chambers.py --tileset dungeon  # single tileset

Requirements:
    pip install google-genai pillow python-dotenv

API key is read from .env at the project root:
    GEMINI_TOKEN=your-key-here
"""

import argparse
import json
import os
import sys
from pathlib import Path

import PIL.Image
from dotenv import load_dotenv
from google import genai
from google.genai import types

load_dotenv(Path(__file__).parent / ".env")

# ── Config ────────────────────────────────────────────────────────────────────

GEMINI_MODEL = "gemini-3.1-pro-preview"   # adjust if needed

TILESETS = [
    "acid", "bubblegum", "dungeon", "flames", "forest",
    "frozen", "honey", "ocean", "rocky", "techy",
]

PROJECT_ROOT = Path(__file__).parent
SHEETS_DIR   = PROJECT_ROOT / "Assets/Art/Spritesheets/Tilesets"
OUTPUT_DIR   = PROJECT_ROOT / "Assets/Chambers"

# ── Tool schema ───────────────────────────────────────────────────────────────

CHAMBER_ITEM_SCHEMA = types.Schema(
    type=types.Type.OBJECT,
    properties={
        "id": types.Schema(
            type=types.Type.STRING,
            description="Unique snake_case ID, format: <tileset>_<concept>_<number>. E.g. dungeon_pillars_01",
        ),
        "description": types.Schema(
            type=types.Type.STRING,
            description="1-2 sentence room description for LLM context. Mention spatial feel and combat implications.",
        ),
        "tags": types.Schema(
            type=types.Type.ARRAY,
            items=types.Schema(type=types.Type.STRING),
            description="2-4 gameplay tags from: open, cover_heavy, melee_favored, ranged_favored, flanking, chokepoint, symmetric, low_visibility, scattered_pillars",
        ),
        "layout": types.Schema(
            type=types.Type.ARRAY,
            items=types.Schema(
                type=types.Type.STRING,
                description="A string of exactly 20 characters: F=floor, W=wall, D=detail, O=open void",
            ),
            description=(
                "Exactly 20 rows of exactly 20 characters each. "
                "Row 0=south edge, row 19=north edge, col 0=west edge, col 19=east edge. "
                "MANDATORY openings (must be F): row0[9-10], row19[9-10], col0[rows9-10], col19[rows9-10]. "
                "All other edge cells must be W. Interior is free."
            ),
        ),
    },
    required=["id", "description", "tags", "layout"],
)

GENERATE_CHAMBERS_TOOL = types.Tool(
    function_declarations=[
        types.FunctionDeclaration(
            name="generate_chambers",
            description=(
                "Generate exactly 8 unique dungeon chamber layouts for a tileset. "
                "Each chamber is a 20x20 tile grid with 2-tile-wide openings on all 4 sides."
            ),
            parameters=types.Schema(
                type=types.Type.OBJECT,
                properties={
                    "tileset_id": types.Schema(
                        type=types.Type.STRING,
                        description="The tileset name (e.g. 'dungeon')",
                    ),
                    "tile_classification": types.Schema(
                        type=types.Type.OBJECT,
                        description="Identifies which tile indices (0-15) serve each role",
                        properties={
                            "primary_floor": types.Schema(
                                type=types.Type.INTEGER,
                                description="Tile index (0-15) for the main walkable floor",
                            ),
                            "primary_wall": types.Schema(
                                type=types.Type.INTEGER,
                                description="Tile index (0-15) for the main solid wall/obstacle",
                            ),
                            "alt_floor": types.Schema(
                                type=types.Type.INTEGER,
                                description="Tile index (0-15) for an alternate floor variant",
                            ),
                            "detail_tiles": types.Schema(
                                type=types.Type.ARRAY,
                                items=types.Schema(type=types.Type.INTEGER),
                                description="Tile indices suitable as decorative details",
                            ),
                        },
                        required=["primary_floor", "primary_wall"],
                    ),
                    "chambers": types.Schema(
                        type=types.Type.ARRAY,
                        items=CHAMBER_ITEM_SCHEMA,
                        description="Exactly 8 chamber definitions",
                    ),
                },
                required=["tileset_id", "tile_classification", "chambers"],
            ),
        )
    ]
)

# ── System prompt ─────────────────────────────────────────────────────────────

SYSTEM_PROMPT = """\
You are a dungeon level designer for a 2D top-down roguelite dungeon crawler.

The attached image is a tileset spritesheet containing exactly 16 tiles (32x32 px each)
arranged in a 4x4 grid, numbered 0-15 in row-major order:
  Row 0 (top):    tiles  0,  1,  2,  3
  Row 1:          tiles  4,  5,  6,  7
  Row 2:          tiles  8,  9, 10, 11
  Row 3 (bottom): tiles 12, 13, 14, 15

Study the spritesheet and identify which tile indices represent floors, walls, and decorations.

Then generate exactly 8 diverse chamber layouts. Rules:
- Every chamber is 20x20 tiles.
- ALL 4 sides must have a 2-tile-wide opening centred at tiles 9-10 of that edge:
    South (row 0):  columns 9 and 10 must be F (never W)
    North (row 19): columns 9 and 10 must be F
    West  (col 0):  rows 9 and 10 must be F
    East  (col 19): rows 9 and 10 must be F
  All other edge cells must be W. This is non-negotiable.
- Make the 8 chambers genuinely different. Vary:
    Obstacle density, layout style (pillars, alcoves, central block, corridors),
    sightline length, combat feel (ranged vs melee vs mixed).
- F=walkable floor, W=solid wall/pillar, D=decorative detail, O=empty void (pits, etc.)
- The border of each chamber (except the 4 openings) must be W to form room walls.
"""

# ── Helpers ───────────────────────────────────────────────────────────────────

def enforce_openings(layout: list) -> list:
    """Guarantee the 4 mandatory 2-tile openings regardless of what Gemini returned."""
    rows = []
    for row in layout[:20]:
        row = str(row).ljust(20)[:20]
        rows.append(list(row))
    while len(rows) < 20:
        rows.append(list("W" * 20))

    rows[0][9]  = "F"; rows[0][10]  = "F"   # south
    rows[19][9] = "F"; rows[19][10] = "F"   # north
    rows[9][0]  = "F"; rows[10][0]  = "F"   # west
    rows[9][19] = "F"; rows[10][19] = "F"   # east

    return ["".join(row) for row in rows]

# ── Core generation ───────────────────────────────────────────────────────────

def generate_for_tileset(client: genai.Client, tileset: str) -> dict:
    png_path = SHEETS_DIR / f"{tileset}.png"
    if not png_path.exists():
        raise FileNotFoundError(f"Spritesheet not found: {png_path}")

    print(f"  Loading {png_path.name} ...")
    image = PIL.Image.open(png_path)

    response = client.models.generate_content(
        model=GEMINI_MODEL,
        contents=[
            SYSTEM_PROMPT,
            image,
            f"This is the '{tileset}' tileset. Analyse it and call generate_chambers with 8 layouts.",
        ],
        config=types.GenerateContentConfig(
            tools=[GENERATE_CHAMBERS_TOOL],
            tool_config=types.ToolConfig(
                function_calling_config=types.FunctionCallingConfig(
                    mode="ANY",
                    allowed_function_names=["generate_chambers"],
                )
            ),
        ),
    )

    if not response.function_calls:
        raise ValueError(
            f"Gemini did not return a function call for '{tileset}'. "
            f"Response: {response.text!r}"
        )

    fc = response.function_calls[0]
    result = dict(fc.args)

    for chamber in result.get("chambers", []):
        chamber["layout"] = enforce_openings(chamber.get("layout", []))

    n = len(result.get("chambers", []))
    if n != 8:
        raise ValueError(f"Expected 8 chambers for '{tileset}', got {n}")

    print(f"  Generated {n} chambers.")
    return result


def save_result(tileset: str, result: dict):
    out_dir = OUTPUT_DIR / tileset
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / "chambers.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False)
    print(f"  Saved -> {out_path}")

# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Generate chamber layouts via Gemini.")
    parser.add_argument("--tileset", help="Run for a single tileset only (e.g. dungeon)")
    args = parser.parse_args()

    api_key = os.environ.get("GEMINI_TOKEN")
    if not api_key:
        print("ERROR: GEMINI_TOKEN not found in .env or environment.", file=sys.stderr)
        sys.exit(1)

    client = genai.Client(api_key=api_key)

    tilesets = [args.tileset] if args.tileset else TILESETS

    failed = []
    for tileset in tilesets:
        print(f"\n[{tileset}] Generating chambers ...")
        try:
            result = generate_for_tileset(client, tileset)
            save_result(tileset, result)
        except Exception as exc:
            print(f"  ERROR: {exc}")
            failed.append(tileset)

    print("\n--- Done ---")
    if failed:
        print(f"Failed tilesets: {', '.join(failed)}")
        sys.exit(1)
    else:
        print("All tilesets completed successfully.")


if __name__ == "__main__":
    main()
