---
name: write-agent-md
description: Write or update an AGENTS.md file for a project. Use when creating, reviewing, or editing agent-facing documentation.
---

# Write Agent.md

Create or update an `AGENTS.md` file that tells an AI agent how to work in this repo.

## Process

1. Read the codebase. Understand what the project is, what it uses, and how it is structured.
2. Read the existing `AGENTS.md` if one exists. Note what works and what does not.
3. Write the `AGENTS.md` using the levers from `writing-for-agents`.

## What to include

- **Project overview** - one paragraph. What it is, what it uses, what makes it different.
- **Architecture** - where things live, how they connect. Point to detailed docs if they exist.
- **File map** - key files and what they do. Short list, not exhaustive.
- **Code standards** - lint command, file limits, style rules. Point to contributing guide if one exists.
- **Security** - any non-obvious rules the agent must not violate.
- **Communication style** - point to the unslop skill.
- **Git** - branching and push rules.

## What to leave out

- Anything the agent already knows (framework basics, language syntax).
- Anything stated elsewhere in the repo (README, ARCHITECTURE.md, CONTRIBUTING.md). Point to it instead of repeating it.
- Placeholders, TODOs, or sections marked "to be documented".

## Leading words

Use compact concepts that anchor behavior:

- **Tight** - short, focused, no waste
- **Red** - what failure looks like
- **Point** - reference instead of repeat
- **Match** - follow existing conventions

## Done when

- The file is shorter than you expect.
- Nothing is stated twice.
- Every line changes agent behavior or points to something that does.