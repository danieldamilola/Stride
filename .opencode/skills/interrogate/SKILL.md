---
name: interrogate
description: "Use for \"interrogate\", \"adversarial review\", \"multi-model review\", \"challenge this\", \"stress test this code\", \"find blind spots\", or \"tear this apart\". Multiple LLM reviewers challenge changes from independent angles."
---

# Interrogate

Spawn two or three parallel reviewer subagents to adversarially review code changes. Each gets the same prompt and rubric. The adversarial signal comes from reviewer diversity, not assigned personas. Agreement across reviewers is high-confidence signal; lone findings are worth reading but lower confidence.

The deliverable is a synthesized verdict. Do NOT auto-apply changes.

## Step 1, Determine Scope

Identify what to review from context:

- If the user points at specific files or a diff, use that
- If on a feature branch, run `git diff main...HEAD` (or the appropriate base branch) for the full changeset
- If the user's message references recent work, gather the relevant files

Package the diff (or file contents) plus any surrounding context files the reviewers need.

## Step 2, State the Intent

Before spawning reviewers, state the intent explicitly. What is this code trying to accomplish? Derive it from the user's message, commit messages, PR description if one exists, and the code itself.

Write one clear paragraph. Reviewers challenge whether the work achieves the intent well, not whether the intent is correct. If unsure about the intent, ask the user before proceeding.

## Step 3, Spawn Reviewers

Launch two or three reviewers in a single message using the Task tool, `subagent_type: general`. If you can use different models for each reviewer, do; if not, vary the review angle instead.

The review brief, identical for all reviewers:

- The stated intent (Step 2)
- The diff or file contents
- The rubric below
- The code-quality lens below

Rubric: correctness (does it do what the intent says), security (inputs, secrets, injection, auth), maintainability (naming, duplication, cohesion, coupling), and regression risk (what existing behavior could this break). Flag anything that would block a real PR.

Code-quality lens: unclear naming, dead code, error handling that swallows failures, mutable state that could be smaller, functions doing more than one thing, and tests that assert without actually exercising the bug path.

Each reviewer returns structured findings: the issue, the file:line, why it matters, and severity (blocker / should-fix / nit).

## Step 4, Synthesize

As results come back:

1. Parse all findings from the reviewers
2. Identify consensus. Findings raised by 2+ reviewers independently are highest signal.
3. Identify lone findings. Worth reading, weight accordingly.
4. Deduplicate. Different reviewers may describe the same issue differently. Merge and note which reviewers raised it.
5. Note disagreements. One reviewer flags something, another says the opposite, that's useful context.

## Step 5, Lead Judgment

You are the lead reviewer, a pragmatic senior engineer, not a neutral aggregator. Reviewers only see a slice of the codebase. You have the full context: the goal, the constraints, which tradeoffs were already considered. Use it.

Categorize every finding:

- **Act on**. Real issues affecting correctness, security, or maintainability given the actual goals. These would block a real PR.
- **Consider**. Legitimate points, but the cost of addressing them now is unclear. Worth the user's attention.
- **Noted**. Technically valid but not actionable. Context-dependent or low-impact at the current stage.
- **Dismissed**. Wrong, nitpicky, or missing context. Brief explanation why.

For each finding: which reviewer(s) raised it, the category, and a one-line rationale.

## Output Format

### Intent
> [The stated intent paragraph from Step 2]

### Reviewers
- Reviewer [label]: [N findings] (one bullet per reviewer)

### Act On
[Findings that should be addressed. For each: description, which reviewers raised it, why it matters.]

### Consider
[Findings worth thinking about. For each: description, which reviewers raised it, tradeoff involved.]

### Noted
[Valid but low-priority. Brief list.]

### Dismissed
[Rejected findings with brief rationale. Shows the user what was filtered out so they can override.]

### Agreement Map
[Where reviewers agreed, where they diverged, and what the pattern tells us.]