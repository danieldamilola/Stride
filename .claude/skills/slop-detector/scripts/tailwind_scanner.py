#!/usr/bin/env python3
"""
tailwind_scanner.py
--------------------
Tailwind/utility-CSS is not a programming language, so it doesn't get an
AST scanner - "slop" here means something different: AI agents tend to
pile on utility classes without composing them, because (same root cause
as everywhere else in this skill) extracting a shared class list requires
noticing repetition across files, which needs a model of the whole
project that the agent usually doesn't have in context.

Three checks, all on raw class-attribute strings (class=, className=):

  tw_bloated_class_list   a single element has an excessive number of
                           utility classes - usually means "component"
                           logic that should be its own component/@apply.
  tw_duplicate_composition the *exact* same long class-string appears 3+
                           times across the scanned files - copy-pasted
                           styling instead of a shared component/class.
  tw_arbitrary_soup        an element leans on 3+ arbitrary-value classes
                           (w-[137px], text-[#ff00aa], top-[13.5%]) - a
                           sign the design system's tokens were bypassed
                           entirely rather than extended.

Usage:
    python3 tailwind_scanner.py <path> --json-out report.json
"""

import argparse
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

CLASS_ATTR_RE = re.compile(r'class(?:Name)?\s*=\s*(["\'`])(.*?)\1', re.DOTALL)
ARBITRARY_RE = re.compile(r"\[[^\]\s]+\]")
SCAN_EXTS = {".html", ".htm", ".jsx", ".tsx", ".vue", ".svelte", ".erb", ".php"}

BLOAT_THRESHOLD = 15
ARBITRARY_THRESHOLD = 3
DUPLICATE_MIN_CLASSES = 6  # ignore short/trivial class strings for dup detection
DUPLICATE_MIN_OCCURRENCES = 3

SEVERITY = {
    "tw_bloated_class_list": "low",
    "tw_duplicate_composition": "medium",
    "tw_arbitrary_soup": "low",
}

REMEDIATION = {
    "tw_bloated_class_list": "Extract this into a component, or collapse the repeated utility group with @apply in a CSS layer.",
    "tw_duplicate_composition": "The exact same class string is copy-pasted across multiple elements/files - extract it into a shared component or an @apply'd class so it only has to change in one place.",
    "tw_arbitrary_soup": "Multiple arbitrary values on one element usually means the design tokens (theme.spacing/colors/etc.) don't cover this case - consider extending the Tailwind config instead of hardcoding magic values inline.",
}


def collect_files(target: Path):
    if target.is_file():
        yield target
        return
    skip_dirs = {".git", "node_modules", ".claude", "build", "dist", "target"}
    for p in target.rglob("*"):
        if p.is_file() and p.suffix.lower() in SCAN_EXTS and not any(part in skip_dirs for part in p.parts):
            yield p


def line_of(text: str, pos: int) -> int:
    return text.count("\n", 0, pos) + 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("target")
    ap.add_argument("--json-out", default=None)
    args = ap.parse_args()

    target = Path(args.target)
    findings = []
    composition_locations = defaultdict(list)
    scanned = 0

    for f in collect_files(target):
        try:
            text = f.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        scanned += 1
        for m in CLASS_ATTR_RE.finditer(text):
            class_str = m.group(2)
            classes = class_str.split()
            line = line_of(text, m.start())

            if len(classes) >= BLOAT_THRESHOLD:
                findings.append({
                    "file": str(f), "line": line, "category": "tw_bloated_class_list",
                    "severity": SEVERITY["tw_bloated_class_list"],
                    "detail": f"{len(classes)} utility classes on one element",
                    "remediation": REMEDIATION["tw_bloated_class_list"],
                })

            arbitrary_hits = ARBITRARY_RE.findall(class_str)
            if len(arbitrary_hits) >= ARBITRARY_THRESHOLD:
                findings.append({
                    "file": str(f), "line": line, "category": "tw_arbitrary_soup",
                    "severity": SEVERITY["tw_arbitrary_soup"],
                    "detail": f"{len(arbitrary_hits)} arbitrary-value classes: {', '.join(arbitrary_hits[:4])}",
                    "remediation": REMEDIATION["tw_arbitrary_soup"],
                })

            if len(classes) >= DUPLICATE_MIN_CLASSES:
                normalized = " ".join(sorted(classes))
                composition_locations[normalized].append((str(f), line))

    for normalized, locs in composition_locations.items():
        if len(locs) >= DUPLICATE_MIN_OCCURRENCES:
            for (file, line) in locs:
                others = [f"{lf}:{ll}" for (lf, ll) in locs if (lf, ll) != (file, line)]
                findings.append({
                    "file": file, "line": line, "category": "tw_duplicate_composition",
                    "severity": SEVERITY["tw_duplicate_composition"],
                    "detail": f"identical class composition repeated {len(locs)}x total, e.g. also at {others[0]}",
                    "remediation": REMEDIATION["tw_duplicate_composition"],
                })

    sev_rank = {"critical": 0, "high": 1, "medium": 2, "low": 3, "info": 4}
    findings.sort(key=lambda x: (sev_rank.get(x["severity"], 9), x["file"], x["line"]))

    counts = {}
    for f in findings:
        counts[f["severity"]] = counts.get(f["severity"], 0) + 1

    report = {
        "scanner": "tailwind_scanner.py",
        "files_scanned": scanned,
        "severity_counts": counts,
        "findings": findings,
    }

    out = json.dumps(report, indent=2)
    if args.json_out:
        Path(args.json_out).write_text(out)
        print(f"[tailwind_scanner] scanned {scanned} files, {len(findings)} findings -> {args.json_out}", file=sys.stderr)
    else:
        print(out)


if __name__ == "__main__":
    main()
