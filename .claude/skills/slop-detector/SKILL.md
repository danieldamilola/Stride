---
name: slop-detector
description: Structural review skill for AI-generated code slop across the whole stack. Wraps ai-slop-detector (flamehaven01) for Python/JS/TS/Go and adds real tree-sitter-based deep analysis for C, C++, C#, Rust, Dart, Swift, Kotlin, and Objective-C, plus a Tailwind/utility-class hygiene pass. Use when asked to review AI-assisted code, inspect changed-code risk, prioritize structural hotspots, plan cleanup, or audit a mixed-language repo for silent errors, phantom dependencies, stub code, debug bleed, duplicated logic, or bloated utility-class soup.
---

# AI Code Slop Detector Skill (extended, multi-language)

This skill is a **structured review loop**, not a last-minute lint step:

1. generate or edit code
2. run a structured review in JSON (across every language actually in the repo)
3. inspect evidence and action classes
4. apply only bounded, confirmed fixes
5. re-run review
6. hand the result to a human with evidence

It has two layers:

- **Core** - [ai-slop-detector](https://github.com/flamehaven01/ai-slop-detector) by
  flamehaven01. Deep AST analysis for **Python** (built in), and **JS/TS**/**Go**
  (optional extras). This layer is unmodified upstream - do not fork its logic here,
  just call its CLI.
- **Extension** (this skill's own `scripts/`) - real tree-sitter AST analysis for
  **C, C++, C#, Rust, Dart, Swift, Kotlin, Objective-C**, plus a separate
  **Tailwind/utility-class** checker for HTML/JSX/TSX/Vue/Svelte. These feed a
  merger that produces one unified JSON report alongside the core tool's output.

Read `references/extended_patterns.md` before fixing anything the extension layer
flags - it documents exactly what each new category means and how the two layers'
severities compare, since they use independently-calibrated scoring.

---

## Install

Core tool:

```bash
pip install ai-slop-detector
pip install "ai-slop-detector[js]"   # optional JS/TS support
pip install "ai-slop-detector[go]"   # optional Go support
slop-detector --version
```

Extension layer (this skill's scripts, for the 8 additional languages + Tailwind):

```bash
pip install tree-sitter tree-sitter-language-pack
```

No install step is needed for the Tailwind checker - it's pure regex, stdlib only.

**What "supports a language" means here, precisely:**

| Layer | Languages | Method |
|---|---|---|
| Core (upstream) | Python | Built-in AST |
| Core (upstream) | JS, TS | tree-sitter, via `[js]` extra |
| Core (upstream) | Go | via `[go]` extra |
| Extension (this skill) | C, C++, C#, Rust, Dart, Swift, Kotlin, Objective-C | tree-sitter, real grammars, `scripts/multilang_scanner.py` |
| Extension (this skill) | Any other tree-sitter-language-pack language | Same script, generic fallback - hedge comments, debug bleed, god-functions, clones still work; language-specific catch/stub detection does not, since that needs a hand-written node-type table (see "Adding a language" below) |
| Extension (this skill) | HTML, JSX, TSX, Vue, Svelte (Tailwind usage) | Regex on `class=`/`className=`, `scripts/tailwind_scanner.py` |

**Phantom-dependency checking is registry-dependent, and is honestly incomplete
outside Python/JS/Rust:**

- Python → PyPI (core tool)
- JS/TS → npm (core tool)
- Rust → crates.io (extension, `phantom_crate` check - real network lookup)
- C#/NuGet, Dart/pub.dev, Swift/SwiftPM+CocoaPods, Kotlin/Maven - **not implemented**.
  Nothing in this skill claims to catch a hallucinated NuGet or pub.dev package.
  If you need that, it's a real gap, not a hidden one - see "Adding a language" below.

---

## Command Selection - Core Tool

Use the smallest surface that matches the job.

### `scan` - full baseline

```bash
slop-detector scan . --format json --output core_report.json
```

### `review` - changed-code review (PR-like tasks default here)

```bash
slop-detector review . --format json
```
Inspect first: `verdict`, `should_fail_build`, `attribution`, `targets`, `actions`, `findings`.

### `pulse` - health summary / hotspot prioritization

```bash
slop-detector pulse . --format json
```

### `sweep <family>` - targeted cleanup planning

```bash
slop-detector sweep dupes . --format json
slop-detector sweep dead-code . --format json
slop-detector sweep unused-deps . --format json
```

### `explain` - mitigation guidance for one pattern

```bash
slop-detector explain empty_except --format json
```

---

## Command Selection - Extension Layer

### Run the multi-language scanner

```bash
python3 scripts/multilang_scanner.py <path> --json-out multilang_report.json
# optional: --god-function-lines 60   (default threshold is 80)
```

### Run the Tailwind checker (only if the repo has HTML/JSX/TSX/Vue/Svelte)

```bash
python3 scripts/tailwind_scanner.py <path> --json-out tailwind_report.json
```

### Merge everything into one report

```bash
python3 scripts/merge_reports.py \
  --base core_report.json \
  --multilang multilang_report.json \
  --tailwind tailwind_report.json \
  --json-out unified_report.json
```

Any of `--base` / `--multilang` / `--tailwind` can be omitted - pass only what you
actually ran. Read `unified_report.json`: it has `total_findings`,
`severity_counts`, `top_hotspots` (files ranked by critical/high count), and a flat
`findings` list with `{file, line, category, severity, detail, remediation, source}`.

---

## Recommended Agent Loop (full-repo, mixed-language)

### 1. Detect what's actually in the repo, then run the matching scanners

Don't assume - check extensions present before deciding which commands to run.
A repo with only `.py` files doesn't need step 1b/1c below; a Flutter app needs
core (nothing, unless there's embedded JS) + multilang (`.dart`) + nothing for
Tailwind (Flutter widgets aren't Tailwind).

```bash
slop-detector review . --format json --output core_report.json          # 1a: py/js/ts/go
python3 scripts/multilang_scanner.py . --json-out multilang_report.json  # 1b: the 8 extended languages
python3 scripts/tailwind_scanner.py . --json-out tailwind_report.json    # 1c: only if HTML/JSX/TSX/Vue present
python3 scripts/merge_reports.py --base core_report.json \
  --multilang multilang_report.json --tailwind tailwind_report.json \
  --json-out unified_report.json                                        # 1d: always
```

### 2. Read `unified_report.json`, start from `top_hotspots`

For each finding, before touching code:
- Re-read the actual file at the given line - the scanners are precise about
  location but do not understand call-site context or intent.
- Cross-check `critical`/`high` findings against `references/extended_patterns.md`
  for what a legitimate exception looks like (e.g. an intentionally-empty catch
  with a comment explaining why is not slop).

Safe agent targets (fix without asking):
- unused/phantom imports and crates
- genuinely empty catch/except blocks with no explanatory comment
- leftover debug print/log statements
- `TODO()`/`unimplemented!()`/`NotImplementedException` stubs - but only convert
  them to a *loud, typed* failure; never invent the missing business logic
- exact clone_cluster pairs - extract to one shared function, update both call sites

Unsafe without human confirmation:
- collapsing a `god_function` - verify the sub-steps are actually independent first
- Tailwind `tw_duplicate_composition` extraction - confirm the elements are meant
  to look identical (some duplication is coincidental, not copy-paste)
- anything the core tool marks `needs_review`
- architecture-level changes

### 3. Re-run steps 1a-1d after edits

Confirm `severity_counts` dropped and the specific finding is gone, not just that
the total count changed (a fix can introduce a new finding elsewhere).

### 4. Escalate with evidence

Human handoff should include: what changed, which commands were run, the
before/after `severity_counts`, and what was deliberately left unfixed and why.

---

## Adding a language

The extension script is intentionally table-driven so this isn't a rewrite:

1. Confirm `tree-sitter-language-pack` has a grammar: `get_parser("<name>")`.
2. Add the file extension to `EXT_TO_LANG` in `scripts/multilang_scanner.py`.
3. Without any other changes you already get: hedge-comment detection, debug-bleed
   detection (if the language's calls decode to plain `head(args)` text - true for
   nearly everything), god-function-by-line-count, and clone detection - **once**
   you also add the function-definition node type(s) to `FUNCTION_NODE_TYPES`.
4. For empty-catch detection, add the catch node type to `CATCH_NODE_TYPES` and
   figure out which of the three body shapes it uses (see the docstring at the top
   of `find_body_statements()` - wrapped / sibling / bare-braces). Get this wrong
   and it silently never fires rather than crashing, so **write a one-file test
   fixture with a deliberately empty catch block and confirm the finding appears**
   before trusting it - that's how the Kotlin/Swift bug in this exact scanner was
   caught during development.
5. For phantom-dependency checking, only add it if the package registry's API is
   reachable from wherever this skill runs - don't add a check you can't verify.

---

## Legacy Note (core tool)

Older core-tool skill revisions used a `/slop`, `/slop-file`, `/slop-gate` slash
framing. Prefer the canonical CLI surfaces: `scan`, `review`, `pulse`, `sweep`,
`explain`, `verify-governance`, `mcp`. This extension does not add new slash
commands - it adds Bash-tool steps 1b/1c/1d above.

