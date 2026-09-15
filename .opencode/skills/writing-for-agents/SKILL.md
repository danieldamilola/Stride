---
name: writing-for-agents
description: Reference for writing agent-facing documents (AGENTS.md, CLAUDE.md, specs, READMEs). Use when creating or editing any document an agent reads.
---

# Writing for Agents

Reference for writing agent-facing documents: a skill, an AGENTS.md / CLAUDE.md, a spec, a runtime prompt, a README, any doc an agent reads. The packaging differs; the writing does not: the same levers make each one predictable, so the agent takes the same process every run rather than producing the same output.

## When to reach for it

Reach for it when creating or editing a skill, or modifying AGENTS.md or CLAUDE.md.

Reach for it by hand for everything else an agent reads: docs, specs and tickets, system and AFK prompts. The test is one question: does an agent read this? It does not matter how the document gets in front of it, whether a pointer names it, a human pastes it, or it simply sits in the repo.

## The two loads

The idea the whole reference turns on is a pair of budgets every document and pointer spends:

- **Context load**: the cost of always-loaded material on the agent's window: an AGENTS.md line, a skill description, anything sitting in context every turn whether or not it fires.
- **Cognitive load**: the cost on you, namely which documents exist and when to reach for each. You are the index.

Once you think in these two loads, most authoring decisions (split or don't, inline or disclose, point or push) become the same trade made in different places.

## The levers

- **Context pointers**: the reference held in context that names out-of-context material and encodes when to reach it. A skill description and an AGENTS.md line naming a doc are the same object; the pointer's wording, not its target, decides how reliably the agent reaches through it.
- **Information hierarchy**: the ladder from in-file step, to in-file reference, to disclosed reference behind a pointer. Progressive disclosure is the move down that ladder so the top stays legible.
- **Completion criteria**: the clarity and demand of each step's done-condition, and the legwork that demand drives; the defence against premature completion.
- **Leading words**: a compact concept already in the model's pretraining that the agent thinks with while running the document. It anchors twice: execution in the body, invocation in the pointer.
- **Pruning**: deletion as the default move. Ask an agent to write instructions and it explains what the model already knows. Every one of those lines is a no-op.

## The no-op test

Delete the line and ask whether the agent's behaviour changed. If not, the line was waste. When a sentence fails, delete the whole sentence rather than trim words from it.

## Common questions

**Can't I just ask the agent to write it for me?**
You can, and it will produce something verbose. Left alone the model explains what it already knows. Use the reference on the draft: a review pass is where most of its value lands.

**I asked an agent to trim a document and it cut the functionality.**
Agents told to "streamline" optimise for length, because length is the thing they can see. The no-op test is behavioural, not aesthetic.

**How do I know when it's done?**
When it works, and you can no longer find duplication, sediment or no-ops. There is no automated eval here.

**Should this live in CLAUDE.md or somewhere else?**
Ask which load you want to pay. CLAUDE.md loads into every session unconditionally; material behind a pointer costs only the pointer's own line until it fires.

## It's working if

- The document gets shorter as it gets better.
- You can point at a leading word and watch it doing work in more than one place.
- Nothing is stated twice, in any form. Duplication is the most reliable sign a document was never tested.
- Reference that only one branch needs sits behind a pointer rather than in the main file.