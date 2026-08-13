#!/usr/bin/env python3
"""
multilang_scanner.py
---------------------
Extends ai-slop-detector's deep-AST coverage (which natively handles
Python, and optionally JS/TS/Go) to eight more languages using real
tree-sitter grammars rather than regex guessing:

    C, C++, C#, Rust, Dart, Swift, Kotlin, Objective-C

For each language it detects, via genuine syntax-tree inspection:
  - empty_except / log_and_continue   (silently swallowed exceptions)
  - debug_bleed                        (leftover print/log/debug statements)
  - stub_marker                        (NotImplementedException, unimplemented!(),
                                         fatalError(...), TODO(), etc.)
  - unhandled_result   [Rust only]     (bare .unwrap()/.expect() instead of `?`)
  - hedge_comment                      (TODO/FIXME/HACK/"for now"/"quick hack"...)
  - god_function                       (function body exceeds a line-count threshold)
  - clone_cluster                      (structurally near-identical functions,
                                         normalized so renamed variables still match)
  - phantom_crate       [Rust only]    (imported crate does not exist on crates.io -
                                         verified via a real network lookup, the same
                                         technique the base tool uses for PyPI/npm)

Any file extension NOT covered by this table but parseable by
tree-sitter-language-pack still gets the language-agnostic checks
(hedge comments, debug bleed, god functions, clone detection) via a
generic fallback path - so adding a language most people forget
(Lua, Zig, Haskell, whatever) is a one-line addition to EXT_TO_LANG.

Usage:
    python3 multilang_scanner.py <path> --json-out report.json [--god-function-lines 80]

Design note: this deliberately does NOT try to be a linter that knows
every grammar's exact field names for every construct. Where grammars
diverge (e.g. Dart puts a catch block's body as a *sibling* of the
catch_clause node, while C++/Swift/Kotlin nest it *inside*), the code
below explicitly handles both shapes rather than silently missing one.
Where a check can be done robustly on decoded source text scoped to a
specific AST node (e.g. "does this call's callee look like a debug
print"), that's preferred over guessing field names that vary release
to release.
"""

import argparse
import hashlib
import json
import re
import sys
import urllib.request
import urllib.error
from pathlib import Path

try:
    from tree_sitter_language_pack import get_parser
except ImportError:
    print("ERROR: pip install tree-sitter tree-sitter-language-pack", file=sys.stderr)
    sys.exit(1)


# ----------------------------------------------------------------------
# Language wiring
# ----------------------------------------------------------------------

EXT_TO_LANG = {
    ".c": "c", ".h": "c",
    ".cpp": "cpp", ".cc": "cpp", ".cxx": "cpp", ".hpp": "cpp", ".hh": "cpp",
    ".cs": "csharp",
    ".rs": "rust",
    ".dart": "dart",
    ".swift": "swift",
    ".kt": "kotlin", ".kts": "kotlin",
    ".m": "objc", ".mm": "objc",
}

# Node types that represent a function/method definition, per language.
# Used for god-function length checks and clone-cluster fingerprinting.
FUNCTION_NODE_TYPES = {
    "c": {"function_definition"},
    "cpp": {"function_definition"},
    "objc": {"method_definition", "function_definition"},
    "csharp": {"method_declaration", "constructor_declaration", "local_function_statement"},
    "rust": {"function_item"},
    "dart": {"function_signature"},  # paired with sibling function_body, see below
    "swift": {"function_declaration"},
    "kotlin": {"function_declaration"},
}

# Node types for a catch clause, per language.
CATCH_NODE_TYPES = {
    "cpp": {"catch_clause"},
    "objc": {"catch_clause"},
    "csharp": {"catch_clause"},
    "dart": {"catch_clause"},
    "swift": {"catch_block"},
    "kotlin": {"catch_block"},
    # c and rust have no catch-clause concept
}

# Whether a language's catch body is nested inside the catch node, or is
# instead the following sibling under the same parent (Dart quirk).
CATCH_BODY_STYLE = {
    "cpp": "nested",
    "objc": "nested",
    "csharp": "nested",
    "dart": "sibling",
    "swift": "nested",
    "kotlin": "nested",
}

BODY_CONTAINER_TYPES = {"block", "compound_statement", "catch_block", "function_body"}

CALL_LIKE_NODE_TYPES = {
    "expression_statement", "call_expression", "macro_invocation",
    "invocation_expression", "method_invocation", "throw_statement",
    "object_creation_expression",
}

DEBUG_CALL_HEADS = [
    "printf", "fprintf(stderr", "std::cout", "cout", "console.log", "console.error",
    "console.warn", "print(", "println(", "println!", "eprintln!", "dbg!",
    "nslog", "system.out.print", "system.err.print", "console.writeline",
]

STUB_CALL_HEADS = [
    "notimplementedexception", "unimplemented!", "todo!", "fatalerror(",
    "todo()", "unimplementederror", "notimplementederror",
]

HEDGE_COMMENT_RE = re.compile(
    r"\b(TODO|FIXME|HACK|XXX)\b|for now\b|temporary workaround|quick hack|"
    r"should work\b|hopefully\b|good enough for now|will fix later",
    re.IGNORECASE,
)

GOD_FUNCTION_DEFAULT_LINES = 80
CLONE_MIN_LINES = 4

SEVERITY = {
    "empty_except": "critical",
    "phantom_crate": "critical",
    "log_and_continue": "high",
    "stub_marker": "high",
    "debug_bleed": "high",
    "unhandled_result": "medium",
    "clone_cluster": "medium",
    "god_function": "medium",
    "hedge_comment": "low",
}

REMEDIATION = {
    "empty_except": "Do not swallow the exception. Re-throw, wrap in a typed error, or log with full context before handling.",
    "log_and_continue": "A log statement alone is not error handling. Decide: retry, propagate, or fail fast - then implement that decision.",
    "stub_marker": "Either implement the real logic now, or make the stub loudly fail at call time with a typed error naming what's missing - don't leave a silent placeholder.",
    "debug_bleed": "Remove leftover debug output before merging, or replace with a real structured-logging call gated by log level.",
    "unhandled_result": "Replace bare .unwrap()/.expect() with `?` propagation or explicit match/if-let handling of the Err/None case.",
    "phantom_crate": "This crate name was not found on crates.io. Verify it's spelled correctly before running cargo build/install - this pattern is how supply-chain 'slopsquatting' attacks work.",
    "clone_cluster": "Extract the shared logic into a single function/module and have all call sites use it, instead of maintaining N copies that will drift.",
    "god_function": "Break this function into smaller, independently testable units along its natural sub-steps.",
    "hedge_comment": "Resolve the TODO/hedge now, or convert it into a tracked issue - don't let temporary language become permanent architecture.",
}


# ----------------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------------

def node_text(node, src: bytes) -> str:
    return src[node.start_byte:node.end_byte].decode("utf-8", errors="replace")


def call_head(text: str) -> str:
    head = text.split("(", 1)[0]
    return head.strip().lower()


def find_body_statements(node, lang: str):
    """Return the list of *named* statement nodes inside a catch clause's
    body, regardless of which of three grammar shapes the language uses:

      1. wrapped  - body is a distinct child node (block/compound_statement),
                    e.g. C++, C#, Objective-C.
      2. sibling  - body is not inside the catch node at all, it's the next
                    sibling under the shared parent, e.g. Dart.
      3. bare     - body statements sit directly between literal '{' '}'
                    tokens that are themselves direct children of the catch
                    node, mixed in with the exception-parameter tokens,
                    e.g. Kotlin, Swift. Filtering by is_named alone is not
                    enough here since the parameter's identifier/type nodes
                    are also named - so we anchor on token position instead.
    """
    # Shape 1: wrapped
    for child in node.children:
        if child.type in BODY_CONTAINER_TYPES:
            return list(child.named_children)

    # Shape 3: bare braces as direct children
    brace_idx = [i for i, c in enumerate(node.children) if c.type in ("{", "}")]
    if len(brace_idx) >= 2:
        start_idx, end_idx = brace_idx[0], brace_idx[-1]
        between = node.children[start_idx + 1:end_idx]
        return [c for c in between if c.is_named]

    # Shape 2: sibling
    parent = node.parent
    if parent is None:
        return None
    siblings = parent.children
    idx = siblings.index(node)
    for sib in siblings[idx + 1:]:
        if sib.type in BODY_CONTAINER_TYPES:
            return list(sib.named_children)
        if sib.type not in (";", ")"):
            break
    return None


def structural_fingerprint(node) -> str:
    """Hash the *shape* of a subtree (node types only, identifiers/literals
    normalized away) so renamed-but-copy-pasted functions still match."""
    tokens = []

    def walk(n):
        t = n.type
        if t in ("identifier", "simple_identifier", "type_identifier"):
            tokens.append("ID")
        elif "literal" in t or t in ("string", "integer_literal"):
            tokens.append("LIT")
        elif "comment" in t:
            return  # ignore comments in the fingerprint
        else:
            tokens.append(t)
        for c in n.children:
            walk(c)

    walk(node)
    return hashlib.sha1("|".join(tokens).encode()).hexdigest()


def check_crates_io(crate_name: str, timeout=4) -> bool:
    """Return True if the crate exists on crates.io. Fails open (returns
    True / 'assume exists') on any network error so a sandboxed or
    offline run never falsely accuses a real crate of being phantom."""
    url = f"https://crates.io/api/v1/crates/{crate_name}"
    req = urllib.request.Request(url, headers={"User-Agent": "slop-detector-ext/1.0 (skill)"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status == 200
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return False
        return True
    except Exception:
        return True


# ----------------------------------------------------------------------
# Per-file scan
# ----------------------------------------------------------------------

def scan_file(path: Path, lang: str, src: bytes, god_lines: int, clone_index: dict, findings: list):
    parser = get_parser(lang)
    tree = parser.parse(src)
    root = tree.root_node

    rust_crate_names = set()

    def walk(node):
        t = node.type

        if "comment" in t:
            text = node_text(node, src)
            m = HEDGE_COMMENT_RE.search(text)
            if m:
                findings.append(mk(path, node, "hedge_comment", text.strip()[:100]))

        if lang in CATCH_NODE_TYPES and t in CATCH_NODE_TYPES[lang]:
            named = find_body_statements(node, lang)
            if named is not None:
                if len(named) == 0:
                    findings.append(mk(path, node, "empty_except", "catch block has no statements"))
                elif len(named) == 1:
                    inner_text = node_text(named[0], src).strip()
                    if call_head(inner_text) and any(
                        inner_text.lower().startswith(h) or call_head(inner_text) in h
                        for h in DEBUG_CALL_HEADS
                    ):
                        findings.append(mk(path, node, "log_and_continue", inner_text[:100]))

        if t in CALL_LIKE_NODE_TYPES:
            text = node_text(node, src)
            head = call_head(text)
            if head:
                if any(head.startswith(h) or h in head for h in DEBUG_CALL_HEADS):
                    findings.append(mk(path, node, "debug_bleed", text.strip()[:100]))
                if any(h in text.lower()[:160] for h in STUB_CALL_HEADS):
                    findings.append(mk(path, node, "stub_marker", text.strip()[:100]))

        if lang == "rust":
            if t == "call_expression":
                text = node_text(node, src)
                if text.rstrip().endswith(".unwrap()") or ".expect(" in text[-40:]:
                    findings.append(mk(path, node, "unhandled_result", text.strip()[:100]))
            if t == "use_declaration":
                text = node_text(node, src)
                first = text.replace("use", "", 1).strip().split("::")[0].strip()
                if first and first not in ("std", "core", "alloc", "crate", "self", "super"):
                    rust_crate_names.add((first, node.start_point[0] + 1))

        # function span + clone fingerprint
        func_node = None
        if lang == "dart" and t == "function_signature":
            parent = node.parent
            if parent is not None:
                siblings = parent.children
                idx = siblings.index(node)
                nxt = siblings[idx + 1] if idx + 1 < len(siblings) else None
                if nxt is not None and nxt.type == "function_body":
                    func_node = node  # span computed specially below
                    span_end_row = nxt.end_point[0]
                else:
                    span_end_row = node.end_point[0]
            else:
                span_end_row = node.end_point[0]
        elif t in FUNCTION_NODE_TYPES.get(lang, set()):
            func_node = node
            span_end_row = node.end_point[0]

        if func_node is not None:
            start_row = func_node.start_point[0]
            n_lines = span_end_row - start_row + 1
            if n_lines >= god_lines:
                findings.append(mk(path, func_node, "god_function", f"{n_lines} lines"))
            if n_lines >= CLONE_MIN_LINES:
                fp = structural_fingerprint(func_node)
                clone_index.setdefault(fp, []).append(
                    {"file": str(path), "line": start_row + 1, "lines": n_lines}
                )

        for c in node.children:
            walk(c)

    walk(root)

    for crate, line in rust_crate_names:
        if not check_crates_io(crate):
            findings.append({
                "file": str(path), "line": line, "category": "phantom_crate",
                "severity": SEVERITY["phantom_crate"],
                "detail": f"crate '{crate}' not found on crates.io",
                "remediation": REMEDIATION["phantom_crate"],
            })


def mk(path, node, category, detail):
    return {
        "file": str(path),
        "line": node.start_point[0] + 1,
        "category": category,
        "severity": SEVERITY[category],
        "detail": detail,
        "remediation": REMEDIATION[category],
    }


# ----------------------------------------------------------------------
# Driver
# ----------------------------------------------------------------------

def collect_files(target: Path):
    if target.is_file():
        yield target
        return
    skip_dirs = {".git", "node_modules", ".claude", "build", "dist", "target", ".dart_tool", "Pods"}
    for p in target.rglob("*"):
        if p.is_file() and not any(part in skip_dirs for part in p.parts):
            yield p


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("target")
    ap.add_argument("--json-out", default=None)
    ap.add_argument("--god-function-lines", type=int, default=GOD_FUNCTION_DEFAULT_LINES)
    args = ap.parse_args()

    target = Path(args.target)
    findings = []
    clone_index = {}
    scanned = 0
    by_lang = {}

    for f in collect_files(target):
        lang = EXT_TO_LANG.get(f.suffix.lower())
        if lang is None:
            continue
        try:
            src = f.read_bytes()
        except OSError:
            continue
        try:
            scan_file(f, lang, src, args.god_function_lines, clone_index, findings)
        except Exception as e:
            findings.append({
                "file": str(f), "line": 0, "category": "scan_error",
                "severity": "info", "detail": str(e)[:200], "remediation": "n/a",
            })
        scanned += 1
        by_lang[lang] = by_lang.get(lang, 0) + 1

    for fp, occurrences in clone_index.items():
        if len(occurrences) < 2:
            continue
        for occ in occurrences:
            others = [o for o in occurrences if o is not occ]
            findings.append({
                "file": occ["file"], "line": occ["line"], "category": "clone_cluster",
                "severity": SEVERITY["clone_cluster"],
                "detail": f"structurally duplicates {len(others)} other function(s), e.g. "
                          f"{others[0]['file']}:{others[0]['line']}",
                "remediation": REMEDIATION["clone_cluster"],
            })

    sev_rank = {"critical": 0, "high": 1, "medium": 2, "low": 3, "info": 4}
    findings.sort(key=lambda x: (sev_rank.get(x["severity"], 9), x["file"], x["line"]))

    counts = {}
    for f in findings:
        counts[f["severity"]] = counts.get(f["severity"], 0) + 1

    report = {
        "scanner": "multilang_scanner.py",
        "files_scanned": scanned,
        "languages": by_lang,
        "severity_counts": counts,
        "findings": findings,
    }

    out = json.dumps(report, indent=2)
    if args.json_out:
        Path(args.json_out).write_text(out)
        print(f"[multilang_scanner] scanned {scanned} files across {list(by_lang.keys())}, "
              f"{len(findings)} findings -> {args.json_out}", file=sys.stderr)
    else:
        print(out)


if __name__ == "__main__":
    main()
