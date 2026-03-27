---
name: add-enemy
description: Use when adding a new enemy, implementing enemy animations, setting up a new enemy type, or onboarding new enemy sprites into the game.
argument-hint: <path to enemy sprites folder e.g. Assets/Art/Sprites/Enemies/Bear/>
disable-model-invocation: true
---

## What This Skill Does

Fully onboards a new enemy into the game: discovers its animation structure, generates all `.anim` clips and an Animator Controller, optionally creates or reuses an AI script, and outputs Inspector setup steps.

Project root: `/Users/iden/Documents/Projects/Jams/Hackiethon`
Scripts root: `Assets/Scripts/Enemies/`
Animations root: `Assets/Animations/Enemies/`

---

## Step 1 — Resolve the Sprites Path

If `$1` was provided, use it as the enemy sprites folder. Otherwise ask:

> "What is the path to the enemy's sprites folder? (e.g. `Assets/Art/Sprites/Enemies/Bear/`)"

Resolve to an absolute path under the project root. Store as `SPRITES_DIR`.

Derive `ENEMY_NAME` from the last folder segment (e.g. `Bear`, `EvilPaladin`).

---

## Step 2 — Discover Animation Structure

Run the following to discover what animation types and frame counts exist:

```bash
find {SPRITES_DIR}/animations -type d | sort
```

For each animation subdirectory found (e.g. `walk-6-frames`, `attack-right`, `scary-walk`, `cross-punch`):
- List the 8 direction subdirs
- Count frames in one direction: `ls {dir}/south/*.png | wc -l`

Present the discovered structure to the user clearly:

```
Found animations for {ENEMY_NAME}:
  • {anim-type-1}: 8 directions × {N} frames
  • {anim-type-2}: 8 directions × {N} frames
```

Also check `{SPRITES_DIR}/rotations/` for static direction sprites.

---

## Step 3 — Choose the AI Script

Read every `.cs` file in `Assets/Scripts/Enemies/` and summarise each to the user:

- File name
- One-line description of what it does
- Whether it's abstract or concrete

Then ask:

> "Which AI script should this enemy use?
> 1. MeleeChaseAI (or another existing script) — configure via Inspector prefixes
> 2. Create a new script — I'll write it now
>
> Reply with the number or the script name."

Store the answer as `AI_CHOICE`.

If they choose an existing script, also ask:
> "What should the walk clip prefix be? (e.g. `bear_walk`)"
> "What should the attack clip prefix be? (e.g. `bear_attack`)"

---

## Step 4 — Generate Animation Clips and Controller

Run the Python generation script below via Bash. Substitute all `{PLACEHOLDERS}` before running.

The script:
1. Reads each PNG `.meta` file to extract the real sprite `internalID` (from `internalIDToNameTable`, key `213:`) — never assumes `21300000`
2. Creates one `.anim` file per animation type per direction (8 × N clips total)
3. Creates one `.controller` file with all states, no transitions
4. Writes `.meta` sidecar files for each generated asset with unique GUIDs

```python
import re, os

PROJECT = "/Users/iden/Documents/Projects/Jams/Hackiethon"
SPRITES_DIR = "{SPRITES_DIR}"   # absolute path
ENEMY_NAME  = "{ENEMY_NAME}"
OUT_DIR     = f"{PROJECT}/Assets/Animations/Enemies/{ENEMY_NAME}"
os.makedirs(OUT_DIR, exist_ok=True)

DIRS     = ["north","north-east","east","south-east","south","south-west","west","north-west"]
DIR_KEYS = ["north","north_east","east","south_east","south","south_west","west","north_west"]

# --- ANIMATION TYPES: fill this dict based on Step 2 discovery ---
# Format: { "clip_prefix": ("folder_name", num_frames, fps, loop) }
ANIM_TYPES = {
    "{WALK_PREFIX}":   ("{WALK_FOLDER}",   {WALK_FRAMES},   8,  True),
    "{ATTACK_PREFIX}": ("{ATTACK_FOLDER}", {ATTACK_FRAMES}, 10, False),
}

def get_sprite_info(meta_path):
    content = open(meta_path).read()
    guid = re.search(r'^guid: (\w+)', content, re.M).group(1)
    m = re.search(r'213: (-?\d+)', content)
    fid = m.group(1) if m else "21300000"
    return guid, fid

def make_anim(name, frames, fps, loop):
    fi = 1.0 / fps
    stop = fi * len(frames)
    curves = "\n".join(
        f"    - time: {i*fi:.8f}\n      value: {{fileID: {fid}, guid: {guid}, type: 3}}"
        for i,(guid,fid) in enumerate(frames))
    pptrs = "\n".join(
        f"    - {{fileID: {fid}, guid: {guid}, type: 3}}"
        for guid,fid in frames)
    return f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!74 &7400000
AnimationClip:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  serializedVersion: 6
  m_Legacy: 0
  m_Compressed: 0
  m_UseHighQualityCurve: 1
  m_RotationCurves: []
  m_CompressedRotationCurves: []
  m_EulerCurves: []
  m_PositionCurves: []
  m_ScaleCurves: []
  m_FloatCurves: []
  m_PPtrCurves:
  - curve:
{curves}
    attribute: m_Sprite
    path:
    classID: 212
    script: {{fileID: 0}}
  m_SampleRate: {fps}
  m_WrapMode: {2 if loop else 0}
  m_Bounds:
    m_Center: {{x: 0, y: 0, z: 0}}
    m_Extent: {{x: 0, y: 0, z: 0}}
  m_ClipBindingConstant:
    genericBindings:
    - serializedVersion: 2
      path: 0
      attribute: 1768191466
      script: {{fileID: 0}}
      typeID: 212
      customType: 23
      isPPtrCurve: 1
    pptrCurveMapping:
{pptrs}
  m_AnimationClipSettings:
    serializedVersion: 2
    m_AdditiveReferencePoseClip: {{fileID: 0}}
    m_AdditiveReferencePoseTime: 0
    m_StartTime: 0
    m_StopTime: {stop:.8f}
    m_OrientationOffsetY: 0
    m_Level: 0
    m_CycleOffset: 0
    m_HasAdditiveReferencePose: 0
    m_LoopTime: {1 if loop else 0}
    m_LoopBlend: 0
    m_LoopBlendOrientation: 0
    m_LoopBlendPositionY: 0
    m_LoopBlendPositionXZ: 0
    m_KeepOriginalOrientation: 0
    m_KeepOriginalPositionY: 1
    m_KeepOriginalPositionXZ: 0
    m_HeightFromFeet: 0
    m_Mirror: 0
  m_EditorCurves: []
  m_EulerEditorCurves: []
  m_HasGenericRootTransform: 0
  m_HasMotionFloatCurves: 0
  m_Events: []
"""

def make_meta(guid):
    return f"""fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 7400000
  userData:
  assetBundleName:
  assetBundleVariant:
"""

# Generate unique GUIDs for clips (deterministic per name)
import hashlib
def make_guid(seed):
    return hashlib.md5(seed.encode()).hexdigest()

all_clips = []
for prefix, (folder, num_frames, fps, loop) in ANIM_TYPES.items():
    for d, dk in zip(DIRS, DIR_KEYS):
        frames = []
        for i in range(num_frames):
            meta = f"{SPRITES_DIR}/animations/{folder}/{d}/frame_{i:03d}.png.meta"
            frames.append(get_sprite_info(meta))
        name = f"{prefix}_{dk}"
        guid = make_guid(f"{ENEMY_NAME}_{name}")
        path = os.path.join(OUT_DIR, f"{name}.anim")
        open(path, 'w').write(make_anim(name, frames, fps, loop))
        open(path+".meta", 'w').write(make_meta(guid))
        all_clips.append((name, guid))
        print(f"  {name}.anim")

# Controller
names = [c[0] for c in all_clips]
guids = {c[0]: c[1] for c in all_clips}
state_fids = {n: 2000000+i+1 for i,n in enumerate(names)}
sm_fid = 1000000
default = names[0]
ctrl_guid = make_guid(f"{ENEMY_NAME}_controller")

child_states = "\n".join(
    f"  - serializedVersion: 1\n    m_State: {{fileID: {state_fids[n]}}}\n    m_Position: {{x: {200+(i%8)*220}, y: {50+(i//8)*100}, z: 0}}"
    for i,n in enumerate(names))

states = "".join(f"""--- !u!1102 &{state_fids[n]}
AnimatorState:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {n}
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: []
  m_StateMachineBehaviours: []
  m_Position: {{x: 50, y: 50, z: 0}}
  m_IKOnFeet: 0
  m_WriteDefaultValues: 1
  m_Mirror: 0
  m_SpeedParameterActive: 0
  m_MirrorParameterActive: 0
  m_CycleOffsetParameterActive: 0
  m_TimeParameterActive: 0
  m_Motion: {{fileID: 7400000, guid: {guids[n]}, type: 2}}
  m_Tag:
  m_SpeedParameter:
  m_MirrorParameter:
  m_CycleOffsetParameter:
  m_TimeParameter:
""" for n in names)

ctrl = f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!91 &9100000
AnimatorController:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {ENEMY_NAME}Animator
  serializedVersion: 5
  m_AnimatorParameters: []
  m_AnimatorLayers:
  - serializedVersion: 5
    m_Name: Base Layer
    m_StateMachine: {{fileID: {sm_fid}}}
    m_Mask: {{fileID: 0}}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 0
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {{fileID: 9100000}}
--- !u!1107 &{sm_fid}
AnimatorStateMachine:
  serializedVersion: 6
  m_ObjectHideFlags: 1
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: Base Layer
  m_ChildStates:
{child_states}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {{}}
  m_StateMachineBehaviours: []
  m_AnyStatePosition: {{x: 50, y: 20, z: 0}}
  m_EntryPosition: {{x: 50, y: 120, z: 0}}
  m_ExitPosition: {{x: 800, y: 120, z: 0}}
  m_ParentStateMachinePosition: {{x: 800, y: 20, z: 0}}
  m_DefaultState: {{fileID: {state_fids[default]}}}
{states}"""

ctrl_path = os.path.join(OUT_DIR, f"{ENEMY_NAME}Animator.controller")
open(ctrl_path,'w').write(ctrl)
open(ctrl_path+".meta",'w').write(make_meta(ctrl_guid))
print(f"  {ENEMY_NAME}Animator.controller")
print("Done!")
```

Write the fully substituted script to a temp file and run it:
```bash
python3 /tmp/gen_{ENEMY_NAME}.py
```

---

## Step 5 — AI Script (if new script requested)

If the user chose to create a new script, ask:
- What is the enemy's behaviour? (melee chase, ranged, patrol, etc.)
- Should it extend `EnemyBase` directly or extend `MeleeChaseAI`?
- Any special attacks or unique logic?

Then write `Assets/Scripts/Enemies/{EnemyName}AI.cs` accordingly, following the patterns in `EnemyBase.cs` and `MeleeChaseAI.cs`. All tunable stats must have public properties with getters and setters. `AttackRange` must be read-only (getter only).

---

## Step 6 — Output Inspector Steps

After generation, output the following steps clearly:

```
## Inspector Setup — {ENEMY_NAME}

**In Hierarchy:** Right-click → 2D Object > Sprite → rename to `{ENEMY_NAME}`

**Add these components:**

1. Sprite Renderer
   - Set a default Sprite from Assets/Art/Sprites/Enemies/{ENEMY_NAME}/rotations/south.png

2. Rigidbody2D
   - Gravity Scale → 0
   - Interpolate → Interpolate
   - Constraints → Freeze Rotation Z

3. Capsule Collider 2D (resize to fit sprite)

4. Animator
   - Controller → Assets/Animations/Enemies/{ENEMY_NAME}/{ENEMY_NAME}Animator.controller

5. Health
   - Max Health → [suggest based on enemy type]

6. {AI_SCRIPT}
   - Walk Prefix → {WALK_PREFIX}
   - Attack Prefix → {ATTACK_PREFIX}
   - Attack Anim Duration → {ATTACK_FRAMES / 10}s
   - Damage Hit Frame → {ATTACK_FRAMES / 10 / 2}s
   - Hit Effect Delay → 0.25s
   - Move Speed → [tune to taste]
   - Attack Range → [tune to taste]
   - Attack Damage → [tune to taste]
   - Hit Color A/B → [choose colours for hit particles]

7. YSorter
   - Sorting Origin Y → -0.3 (adjust to sort from feet)

**Layer:** Set to Enemy layer

**Prefab:** Drag to Assets/Prefabs/Enemies/ when done
```

---

## Notes

- Always read `.meta` files for real sprite fileIDs — never hardcode `21300000` (Unity 6 uses `internalIDToNameTable`)
- The controller is hand-crafted YAML. If the `UnityEditor.Graphs` null ref error appears in the Animator window, it's editor-only and won't affect builds. Recreate the controller through the Unity UI to remove it.
- If an animation folder has a non-standard number of frames per direction, adjust `num_frames` in `ANIM_TYPES` per animation type
- Clip names must follow the pattern `{prefix}_{direction_key}` exactly — the AI scripts call `animator.Play()` with these names
- GUIDs are generated via MD5 hash of the clip name — deterministic and unique within the project
