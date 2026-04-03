---
name: build-understand-project-skill
description: Use when someone wants to create or update an understand-project skill for the current project, generate project onboarding context, document the project structure for future Claude sessions, or refresh an outdated understand-project skill.
---

## What This Skill Does

Reads the current project, builds a tailored `/understand-project` skill at `.claude/skills/understand-project/SKILL.md`, and optionally installs a stale-marker hook that flags the skill when Claude modifies architectural files.

**Two modes:**
- **Create** — no existing skill found → explore, interview, write from scratch
- **Update** — existing skill found → read it, diff against current project state, update only what changed

---

## Step 1: Detect Mode

Check if `.claude/skills/understand-project/SKILL.md` already exists.

**If it exists:** Read it fully. You are in **Update mode** — go to Step 2B.
**If it doesn't exist:** You are in **Create mode** — go to Step 2A.

---

## Step 2A: Explore the Project (Create mode)

Systematically read the project to understand it. Work through these in order:

**Identity & build:**
- `package.json` / `Cargo.toml` / `go.mod` / `pyproject.toml` / `build.gradle` — framework, dependencies, scripts
- `vite.config.*`, `next.config.*`, `webpack.config.*` — build tool and config
- `README.md` — any stated architecture or conventions
- Git remote: `git remote get-url origin`

**Structure:**
- Top-level directory listing
- `src/` or equivalent — subdirectory layout
- Identify: where does data live? where do pages/routes live? where do components/modules live?

**Data layer:**
- Data files (e.g. `src/data/*.js`, `*.json`, database schemas, ORM models)
- Read 2–3 representative data files to understand field shapes and conventions
- How is data consumed? (imported directly, fetched via API, read from DB)

**Routing / navigation:**
- Router config file (e.g. `App.jsx`, `router.ts`, `pages/` directory, `routes/`)
- List all routes and what they render
- Is routing file-based or manually registered?

**Components / modules:**
- Read the main entry point
- Skim the most important 3–5 components or modules — enough to understand the patterns
- Identify reusable shared components vs page-level components

**Conventions:**
- Naming patterns (files, variables, CSS)
- State management approach
- Any shared utilities or helpers worth knowing about
- Asset organisation (where do images, fonts, etc. live)

---

## Step 2B: Diff Against Current State (Update mode)

Read the existing skill to see what it currently documents. Then re-read the key files it references to check for changes. Focus on:

- Has the directory structure changed?
- Have data file schemas changed (new fields, removed fields, new files)?
- Have routes been added or removed in the router config?
- Have new components or patterns been introduced that aren't documented?
- Are there any `⚠️ STALE` markers at the top of the skill from the hook?

Summarise what has changed vs what is still accurate. Only rewrite the sections that are stale — preserve accurate sections verbatim. Tell the user what you're updating before writing.

---

## Step 3: Interview for Gaps

After exploring, ask about things the code can't tell you. One round at a time.

**Round 1 — Purpose & Audience**
> "In one sentence: what does this project do, and who uses it?
> And what are the most common tasks someone would do in a new Claude session here — e.g. 'add a new page', 'add a new API endpoint', 'modify the data schema'?"

**Round 2 — Gotchas & Conventions**
> "Are there any non-obvious conventions, constraints, or gotchas I should document?
> For example: 'always register routes manually in X file', 'data keys must match exactly', 'images go in a specific folder', etc."

**Round 3 — Key File List for Stale Hook (if setting up hook)**
> "I'll suggest a stale-marker hook that watches for changes to architectural files. Based on reading the project, I'd watch these files/patterns:
> `{list of identified key files}`
>
> Anything to add or remove from this list?"

Skip rounds where you already have the answer from reading the project. Don't re-ask what you know.

---

## Step 4: Write the `understand-project` Skill

Write to `.claude/skills/understand-project/SKILL.md`. Tailor every section to this specific project — do not use generic placeholder text.

**Structure to follow:**

```markdown
---
name: understand-project
description: Use when starting a new session on this project, when asked to understand the project context, or before making changes to {main common task}.
---

## What to Read First

List the 2–5 files that give the most context. These are read at the start of every session.
Explain in one line what each file contains.

---

## Project Overview

**Framework / stack:** {specific versions and tools}
**Entry point:** {file}
**Deployment:** {how and where}

**Directory structure:**
{annotated tree — only the directories that matter, with one-line explanations}

---

## {Main Data / Content Layer}

{Explain how data is structured, where it lives, and how it's consumed.}
{Include the actual schema/shape with field-level comments.}
{Explain the naming conventions and any constraints (e.g. key must match exactly).}

---

## Routing / Navigation

{Explain the routing approach — file-based, manual, etc.}
{List all routes with what they render.}
{Explain any naming conventions between data and routes.}

---

## {Other Major Pattern — repeat section as needed}

{Each section covers one distinct pattern in the codebase that a new session would need to know.}

---

## How to {Most Common Task}

Step-by-step recipe for the task someone is most likely to perform.
Number the steps. Name the exact files to touch.

---

## How to {Second Common Task}

Same pattern.

---

## Key Conventions

Bullet list of conventions, gotchas, and constraints that aren't obvious from reading the code.
These are the things the user told you in the interview, plus anything you found surprising.

---

## Notes

Any remaining caveats, non-obvious dependencies, or things that would trip up a new Claude session.
```

**Quality rules:**
- Every file path mentioned must be real and verified against what you read.
- Every schema field must reflect the actual current data — read the real files, don't guess.
- Routes section must list actual current routes — read the router file.
- "How to" recipes must name every file that needs to be touched, in order.
- No generic advice ("make sure to test your changes") — only project-specific facts.
- Keep total length under 400 lines. Move exhaustive field lists or examples to a supporting file `understand-project-reference.md` in the same directory if needed.

---

## Step 5: Set Up the Stale-Marker Hook (Optional)

Ask:
> "Do you want a stale-marker hook? When Claude edits any of the key architectural files, it'll prepend a `⚠️ STALE` warning to the understand-project skill so you see it at the start of the next session. It's pure bash — zero Claude token cost."

If yes:

**Identify the key file patterns** from your exploration. These are files whose changes would invalidate the understand-project skill — typically:
- The router/routes config file
- Data schema files
- The main entry point
- Any config file that defines conventions

**Write or update `.claude/settings.json`** in the project root. Add a `PostToolUse` hook:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Edit|Write",
        "hooks": [
          {
            "type": "command",
            "command": "bash -c 'SKILL=\".claude/skills/understand-project/SKILL.md\"; CHANGED=\"$CLAUDE_TOOL_INPUT_FILE_PATH\"; PATTERNS=\"{pipe-separated key file patterns}\"; if [ -f \"$SKILL\" ] && echo \"$CHANGED\" | grep -qE \"$PATTERNS\"; then grep -q \"STALE\" \"$SKILL\" || sed -i \"\" \"1s|^|> ⚠️ STALE — Claude edited an architectural file (${CHANGED}). Re-run /build-understand-project-skill to update.\\n\\n|\" \"$SKILL\"; fi'"
          }
        ]
      }
    ]
  }
}
```

Replace `{pipe-separated key file patterns}` with the actual patterns for this project, e.g.:
`src/App\\.jsx|src/data/|src/routes/|package\\.json`

**If `.claude/settings.json` already exists:** read it first, then merge the new hook into the existing `PostToolUse` array — do not overwrite existing hooks.

**macOS note:** The `sed -i ""` syntax is macOS-specific. On Linux it would be `sed -i`. Since this project is on macOS, use `sed -i ""`.

---

## Step 6: Confirm

Tell the user what was written:

```
✓ Written: .claude/skills/understand-project/SKILL.md
{✓ Hook installed in .claude/settings.json — watches: {key file patterns}}

The skill documents:
- {bullet: what was captured, e.g. "Data schema for X and Y"}
- {bullet: routing — N routes}
- {bullet: recipes for: add X, modify Y}
- {bullet: N conventions and gotchas}

Re-run /build-understand-project-skill any time to refresh.
{If hook installed: The hook will also prepend a ⚠️ STALE warning when Claude edits key files.}
```

---

## Notes

- In Update mode, preserve accurate sections verbatim — only rewrite what has actually changed.
- Never document things you haven't verified by reading the actual files.
- The stale hook uses `$CLAUDE_TOOL_INPUT_FILE_PATH` — this is populated by Claude Code's PostToolUse hook environment. If it isn't available in the user's Claude Code version, the hook will silently no-op (the `-f "$SKILL"` guard prevents errors).
- If the project has no `.claude/` directory yet, create it before writing the skill.
- The hook's `sed -i ""` is macOS syntax. If the user is on Linux, use `sed -i` without the empty string argument.
