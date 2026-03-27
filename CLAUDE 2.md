# Hackiethon — Claude Code Project Config

## Project
2.5D top-down dungeon crawler, Unity 6 (6000.3.10f1), URP. Game jam project.

## Skills

### `/add-enemy` — Add a new enemy
Trigger: "add a new enemy", "implement enemy animations", "set up the X enemy"
- Discovers animation structure from a sprites folder path
- Reads existing AI scripts and asks whether to reuse or create new
- Generates all `.anim` clips and Animator Controller via Python
- Outputs Inspector setup steps

Usage: `/add-enemy Assets/Art/Sprites/Enemies/MyEnemy/`
