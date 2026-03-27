---
name: understand-project
description: Use when someone asks to understand the project, explain what this project is about, give an overview of the codebase, or summarize the project context.
---

## What This Skill Does

Reads all files in `.claude/context/` and the broader project structure to build a complete understanding of what the project is, then delivers a clear summary to the user.

## Steps

1. Read every file in `.claude/context/` — start with `CONTEXT.md` and any other files present.
2. Glob for top-level project files to understand the tech stack: `**/*.csproj`, `**/*.sln`, `**/*.json`, `**/*.md` (limit depth to avoid noise).
3. Read `CLAUDE.md` if it exists at the project root.
4. Based on what you read, produce a structured project summary using the output format below.

## Output Format

```
## Project: [Name]

**Type:** [e.g., Hackathon project / Game / Web app]
**Theme / Goal:** [one sentence]

### Concept
[2–4 sentences describing the core idea]

### How the LLM is Integrated
[Explain the LLM's role in the project — what it generates, when it's called, what it returns]

### Tech Stack
- [Language / Engine]
- [Key libraries or frameworks]
- [External APIs or services]

### Architecture Overview
[Brief description of how the pieces fit together — client, server, LLM calls, data flow]

### Key Files / Directories
- `path/` — [what lives here]

### Current Status
[What's done, what's in progress, any known gaps — only if inferable from context]
```

## Notes

- Do NOT summarize from memory — always read the files fresh.
- If `.claude/context/` contains multiple files, read all of them before writing the summary.
- If the project has no CONTEXT.md or context directory, fall back to reading `README.md`, top-level source files, and `CLAUDE.md`.
- Keep the summary concise and developer-focused. Skip fluff.
