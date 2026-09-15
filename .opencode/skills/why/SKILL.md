---
name: why
description: "Use for 'why does X work this way', 'why we picked Y', design rationale, regressions, postmortems, or data-backed thresholds. Investigates motivation and intent from git history, docs, and external sources, then returns a cited read on decisions and tradeoffs. Use how for runtime behavior."
---

# Why

Investigate the motivation and intent behind code. Why was it built this way? What edge cases were considered? What product, business, or operational constraints shaped the design? What alternatives were rejected, and why?

Companion to the `how` skill. `how` answers what the code does and how it works. `why` answers what forces led to its shape.

## Operating Posture

Operate as a careful, cautious, precise investigator. Think like a detective piecing together a historical case from fragmentary records. When the record is thin, say so.

Concretely:

- **Evidence before narrative.** Collect the pieces first, then see what story they support. Never pick a story and recruit the evidence that fits it.
- **Precision over polish.** Prefer the exact quote and citation over a smooth paraphrase. A reader should be able to follow any claim back to its source and verify it in under a minute.
- **Consider what you haven't seen.** The evidence you find is a sample, not the whole truth. Before concluding, ask what you would expect to see if an alternative explanation were true, and whether you looked for it.
- **Name the gaps.** If a thread goes cold, a source isn't searchable, or a question has no answer, document the gap. Don't paper over it with an authoritative-sounding guess.
- **Hedge on purpose.** When evidence is indirect, your language should signal it ("appears to", "likely", "suggests"). Confidence-matching phrasing is a feature of the output, not a stylistic choice.
- **No shortcut by code-reading.** The code tells you what it does, rarely why it exists. Resist inferring intent from code shape.

This posture is the working method, not a disclaimer.

## Core Epistemics

Build a patchwork understanding from fragmented historical evidence. Tickets go stale. Commit messages lie. People change their minds between the PR description and the implementation.

Be ruthlessly honest about what you know versus what you're inferring. The goal is not a satisfying story; it is to surface evidence, calibrate confidence, and let the user decide.

Principles:

- **Cite everything.** Every claim about intent should reference a specific commit hash, PR number, ticket ID, doc URL, or code comment. If you can't cite it, it's inference, not fact, and must be labeled as such.
- **Prefer "appears to" over "because".** Hedge when evidence is indirect. Reserve confident language for direct, explicit evidence.
- **Surface contradictions.** If two sources disagree, show both. Don't quietly pick the one that fits your narrative.
- **Acknowledge gaps.** If a question has no answer in any source you searched, say so. An honest "we couldn't find out why" beats a confident guess.
- **Multiple hypotheses are valid.** When the evidence fits several stories, present them all with the evidence for each. Let the user triangulate.
- **Beware rationalization.** Code that makes sense today may have been written for reasons that no longer apply, or for no good reason at all. Don't retrofit intent.

## Step 1. Understand the Target and the Question

Parse what the user is asking. The **target** is usually a chunk of code, a pattern, a feature, or a named design decision. The **question** is usually one of:

- "Why was X designed this way?" Design rationale.
- "Why do we do X instead of Y?" Tradeoff or alternatives.
- "What edge cases motivated this?" Defensive reasoning.
- "What business or product constraint led to this?" External forcing function.
- "Why does this code still exist?" Dead-code territory.
- "What's the history of X?" Broad archaeological sweep.

If the target is vague, make your best guess from conversation context (open files, recent edits, what was just discussed). State your interpretation briefly so the user can redirect if you're off, then proceed.

## Step 2. Establish the Code Anchor

Before digging, anchor the investigation in concrete code. You need the relevant file path(s) and line range(s), the key symbols, and an initial commit list.

```bash
# Blame target lines for last-touch commits
git blame -L <start>,<end> <file>

# Full file history, with patches, through renames
git log --follow -p -- <file>

# Last N commits touching the file, PR numbers visible
git log --oneline -20 -- <file>

# Extract PR numbers from a commit message
git log -1 --format=%B <commit>
```

Pull PR bodies and discussion via `gh` for any substantive commits:

```bash
gh pr view <number> --json title,body,author,createdAt,mergedAt,labels,closingIssuesReferences,comments,reviews
```

## Step 3. Search the Evidence Sources

Historical context spreads across several evidence categories. You cannot predict from the question alone which one holds the answer, so search them all and report null results as first-class evidence.

1. **Source control history.** Git log/blame, `gh` PR views, code comments, test names. The most trustworthy source; it ties directly to the diff that shipped.
2. **Repo documents.** Search the repo for ADRs, specs, design docs, PRDs, CONTEXT.md, README history, postmortems. Where the why is written out before it becomes code.
3. **Issue / ticket tracker.** GitHub issues via `gh` when the repo is on GitHub. Customer requests, compliance deadlines, scope changes.
4. **Web / external sources.** `websearch` for docs, blog posts, forum threads, or public discussions tied to the project, library, or feature.
5. **Local record.** Anything the repo keeps outside code: changelogs, release notes, config comments, migration scripts.

Use parallel subagents for the bulk reading where the sources are large. Each source gets one searcher so their results stay clean. A null result from a category is evidence the decision was not recorded there, a useful fact in itself. Only skip a category with an explicit, written justification.

Do not answer from anticipation ("it's pure feature code, the tracker won't have anything"). Run the search; let the null result speak.

## Step 4. Synthesize

Build the final answer directly from the evidence you gathered, following the posture and epistemics above. The confidence separation is the product; do not rewrite the hedges to sound more authoritative.

## Output Format

**The Question.** Restate what the user asked, concisely.

**The Code in Question.** File paths, line ranges, and key symbols. One or two lines.

**What We Found (direct evidence).** Claims with explicit citations (PR #, ticket ID, doc URL, commit hash, code comment with file:line). Each bullet is a thing we have textual evidence for. Use present tense and quote or paraphrase the source.

**What We Can Reasonably Infer.** Claims well-supported by indirect evidence or combinations of signals, but not explicitly stated anywhere. Each bullet must explain the inference chain: "Given A and B, it's likely that C." Use hedged language.

**Competing Hypotheses.** If the evidence fits multiple stories, list them. For each, give the hypothesis, the evidence for it, and the evidence against it. Don't force a winner when the record doesn't support one. (Skip if there's a clear answer.)

**What We Don't Know.** Explicit gaps. Questions the user asked that the evidence didn't answer. Sources we searched and came up empty. Be specific. "We searched the issue tracker for 'rate limit' and found no ticket discussing this threshold" is more useful than "we don't know why."

**Sources Consulted.** One line per source searched, including the ones that returned nothing. Format: `- <Source>: <what was searched>. <what was found, or "no relevant results," or "skipped. reason">.`

## Common Failure Modes to Avoid

- **Confident storytelling.** A plausible narrative built from thin evidence. A bullet with no citation goes in "inferred" or "hypotheses," not "what we found."
- **Citing the code as evidence for its own intent.** "Handles the null case because it checks for null" is mechanics, not motivation. Motivation comes from an external source or is labeled as inference.
- **Recency bias.** Assuming the most recent commit is authoritative. The current shape is often the accretion of many earlier decisions. Trace back.
- **Sycophantic agreement.** If the user suggests a reason ("I assume this is for performance?"), treat it as a hypothesis and check the evidence independently.
- **Skipping the gaps section.** An honest accounting of what you couldn't find out is part of the value.