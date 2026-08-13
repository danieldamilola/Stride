#!/usr/bin/env python3
"""
merge_reports.py
------------------
Combines up to three JSON reports into one unified findings list so
Claude only has to read a single file per scan, regardless of how many
languages the repo mixes:

  --base       output of `slop-detector scan/review` (ai-slop-detector's
               own Python/JS/TS/Go engine)
  --multilang  output of multilang_scanner.py (C/C++/C#/Rust/Dart/Swift/
               Kotlin/Objective-C via tree-sitter)
  --tailwind   output of tailwind_scanner.py (utility-class checks)

All three are optional - pass whichever ones you actually ran. Output
schema, one dict per finding:
    {file, line, category, severity, detail, remediation, source}

Usage:
    python3 merge_reports.py --base base.json --multilang ml.json \
        --tailwind tw.json --json-out unified_report.json
"""

import argparse
import json
from pathlib import Path

SEV_RANK = {"critical": 0, "high": 1, "medium": 2, "low": 3, "info": 4}


def load(path):
    if path is None:
        return None
    p = Path(path)
    if not p.exists():
        return None
    try:
        return json.loads(p.read_text())
    except json.JSONDecodeError:
        return None


def from_base_tool(data):
    """Normalize ai-slop-detector's native schema (file_results /
    js_file_results / go_file_results, each with pattern_issues)."""
    out = []
    if not data:
        return out
    for bucket in ("file_results", "js_file_results", "go_file_results"):
        for fr in data.get(bucket) or []:
            for issue in fr.get("pattern_issues") or []:
                out.append({
                    "file": issue.get("file", fr.get("file_path", "")),
                    "line": issue.get("line", 0),
                    "category": issue.get("pattern_id", "unknown"),
                    "severity": issue.get("severity", "medium"),
                    "detail": issue.get("message", ""),
                    "remediation": issue.get("suggestion", ""),
                    "source": "ai-slop-detector",
                })
            for dep in fr.get("hallucination_deps") or []:
                if isinstance(dep, dict):
                    out.append({
                        "file": dep.get("file", fr.get("file_path", "")),
                        "line": dep.get("line", 0),
                        "category": "phantom_import",
                        "severity": "critical",
                        "detail": dep.get("message", str(dep)),
                        "remediation": "Verify this package actually exists on the registry before installing - do not blindly run the install command.",
                        "source": "ai-slop-detector",
                    })
    return out


def from_extension_scanner(data, tool_name):
    out = []
    if not data:
        return out
    for f in data.get("findings") or []:
        f = dict(f)
        f.setdefault("remediation", "")
        f["source"] = tool_name
        out.append(f)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base")
    ap.add_argument("--multilang")
    ap.add_argument("--tailwind")
    ap.add_argument("--json-out", required=True)
    args = ap.parse_args()

    findings = []
    findings += from_base_tool(load(args.base))
    findings += from_extension_scanner(load(args.multilang), "multilang_scanner")
    findings += from_extension_scanner(load(args.tailwind), "tailwind_scanner")

    findings.sort(key=lambda x: (SEV_RANK.get(x.get("severity"), 9), x.get("file", ""), x.get("line", 0)))

    severity_counts = {}
    by_file = {}
    for f in findings:
        severity_counts[f.get("severity", "info")] = severity_counts.get(f.get("severity", "info"), 0) + 1
        by_file.setdefault(f.get("file", "?"), []).append(f)

    hotspots = sorted(
        by_file.items(),
        key=lambda kv: sum(1 for f in kv[1] if f.get("severity") in ("critical", "high")),
        reverse=True,
    )[:10]

    report = {
        "total_findings": len(findings),
        "severity_counts": severity_counts,
        "files_with_findings": len(by_file),
        "top_hotspots": [{"file": f, "finding_count": len(items)} for f, items in hotspots if items],
        "findings": findings,
    }

    Path(args.json_out).write_text(json.dumps(report, indent=2))
    print(f"[merge_reports] {len(findings)} total findings across {len(by_file)} files -> {args.json_out}")


if __name__ == "__main__":
    main()
