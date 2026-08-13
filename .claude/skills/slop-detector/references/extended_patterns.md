# Extended Pattern Playbook

Guidance for the categories produced by `multilang_scanner.py` and
`tailwind_scanner.py`. The core tool's own categories (`empty_except` for
Python, `hallucination_deps`, etc.) are documented via `slop-detector explain
<pattern>` — use that instead of duplicating it here.

Severities across the two layers are **independently calibrated** — a `high`
from `multilang_scanner.py` and a `high` from the core Python engine are not
guaranteed to represent identical risk. Treat severity as within-layer
prioritization, not a single global scale, until you've spot-checked both on
this specific repo.

---

## empty_except (C++, C#, Rust N/A, Dart, Swift, Kotlin, Objective-C)

A catch/do-catch block with zero statements in its body. The exception is
caught and the information is gone.

**Before fixing:** an empty catch guarding a genuinely-safe-to-ignore failure
*can* be legitimate (e.g. best-effort cleanup in a `finally`-less language).
The distinguishing signal is a comment explaining why — if there's no comment,
treat it as slop; if there is one, leave it.

**Fix:** re-throw, wrap in a typed/domain error, or at minimum log with the
original exception attached (not just a static string).

## log_and_continue

A catch block whose only statement is a debug print/log call. Slightly better
than `empty_except` because the failure is at least visible somewhere, but
still not handled — execution continues as if nothing happened.

**Fix:** decide explicitly — retry, propagate, or fail fast — and implement
that decision instead of a bare log line.

## debug_bleed

A `printf`/`console.log`/`NSLog`/`println`/`Console.WriteLine`/etc. call that
reads like a leftover debugging aid rather than intentional output (the
detector doesn't try to guess intent beyond "this looks like a debug call
site" — check it before deleting; some of these are real logging).

**Fix:** remove, or replace with a real structured-logging call gated by log
level, so it can stay in the codebase without printing on every run.

## stub_marker

`NotImplementedException`, `unimplemented!()`, `todo!()`, `fatalError(...)`,
`TODO()`, `UnimplementedError` — code that looks complete but panics if
reached.

**Fix:** either implement the real logic now, or make sure the failure is
loud and typed (already usually true for these constructs) with a message
naming exactly what's missing — don't downgrade this to a silent no-op.

## unhandled_result (Rust only)

`.unwrap()` / `.expect(...)` called on a `Result`/`Option` without any
surrounding handling. AI-generated Rust leans on `.unwrap()` heavily because
it's the shortest path to code that type-checks, not because the failure mode
was considered.

**Fix:** propagate with `?` where the caller can handle it, or use
`match`/`if let` to handle the `Err`/`None` case explicitly. `.expect("clear
message")` in a genuinely-unreachable branch (with a comment saying why it's
unreachable) is fine — the detector flags the call site, not the outcome.

## phantom_crate (Rust only)

A `use` statement referencing a crate name that returned a 404 from
crates.io at scan time. This is the same failure mode as PyPI/npm
slopsquatting: an LLM hallucinates a plausible-sounding crate name, and if
someone has since registered that exact name with malicious code, `cargo
build` pulls it in.

**Fix:** verify the correct crate name before building. Do not add a
`[patch]` or vendor entry to make the phantom name "work" — find what the
code actually meant to import.

## clone_cluster

Two or more functions whose *structure* is identical after normalizing away
identifier and literal names — i.e. copy-pasted logic with the variables
renamed, which naming-based duplicate detection would miss entirely.

**False-positive check:** very short functions (below the scanner's 4-line
floor are already excluded, but a 4-6 line match can still be a coincidence —
e.g. two unrelated getters that both do `return self.x + self.y`-shaped
arithmetic). Read both sites before merging.

**Fix:** extract the shared logic into one function/module, update every
call site to use it, delete the duplicates.

## god_function

A function/method spanning more lines than the configured threshold (default
80). Line count is a proxy, not a direct measure of complexity — a
mechanically repetitive 90-line function (e.g. a big switch/when mapping
codes to strings) is architecturally fine and shouldn't be force-split.

**Fix:** only split along genuine sub-steps the function already has —
if you can't name 2+ independently-testable pieces, it's not actually a god
function, it's just long, and splitting it for the sake of a metric makes it
worse, not better.

## hedge_comment

`TODO`, `FIXME`, `HACK`, `XXX`, or phrases like "for now" / "temporary
workaround" / "should work" / "hopefully" inside a comment. This is the
lowest-severity category on purpose — hedge language in a comment is a
signal to investigate, not a bug by itself.

**Fix:** resolve it now if it's small, or convert it into a tracked issue
referenced by the comment (`// TODO(JIRA-1234): ...`) if it's not — don't
just delete the comment and leave the underlying gap.

---

## Tailwind / utility-class categories

### tw_bloated_class_list

15+ utility classes on a single element. Not wrong by itself, but usually
means the element has grown enough responsibility that it should be its own
component, or the repeated group should be collapsed with `@apply`.

### tw_arbitrary_soup

3+ arbitrary-value classes (`w-[137px]`, `text-[#ff00aa]`, `top-[13.5%]`) on
one element. Each individual arbitrary value can be legitimate; three or more
stacked on the same element usually means the design system's tokens don't
cover this case at all, and it was easier for the generating model to invent
pixel values than to extend the Tailwind config.

**Fix:** if the same arbitrary value shows up more than once across the
codebase, add it to `tailwind.config` as a named token instead of repeating
the raw value.

### tw_duplicate_composition

The exact same class string (order-independent) appears on 3+ elements.
Copy-pasted styling instead of a shared component or class.

**False-positive check:** some duplication is coincidental — a `<button>`
and an unrelated `<div>` can legitimately end up with the same 6 layout
classes. Judge by whether the elements are conceptually the same *kind* of
thing, not just whether the string matches.

**Fix:** extract into a component (React/Vue/Svelte) or an `@apply`'d class
in a CSS layer, and point every occurrence at it.
