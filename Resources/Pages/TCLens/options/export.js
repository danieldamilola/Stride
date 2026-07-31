/**
 * @file export.js
 * @description Analysis export to text file for T&C Lens.
 * @module options/export
 */

import { getRiskLevel, formatDate } from "./utils.js";

/**
 * Export an analysis result as a downloadable .txt file.
 *
 * @param {Object} analysis - The analysis entry to export.
 */
export function exportAnalysis(analysis) {
  const lines = [
    `T&C Lens Analysis: ${analysis.title || analysis.domain}`,
    `URL: ${analysis.url}`,
    `Date: ${formatDate(analysis.analyzedAt)}`,
    `Risk Score: ${analysis.risk_score} (${getRiskLevel(analysis.risk_score)})`,
    "",
    `Summary: ${analysis.summary}`,
    "",
    "Findings:",
    ...analysis.findings.map((f, i) =>
      [
        `${i + 1}. ${f.title} [${f.importance.toUpperCase()} RISK]`,
        `   ${f.description}`,
        f.quote ? `   Quote: "${f.quote}"` : "",
      ]
        .filter(Boolean)
        .join("\n"),
    ),
  ];

  const blob = new Blob([lines.join("\n")], { type: "text/plain" });
  const url = URL.createObjectURL(blob);

  const a = document.createElement("a");
  a.href = url;
  a.download = `tc-lens-analysis-${analysis.domain || "export"}.txt`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
