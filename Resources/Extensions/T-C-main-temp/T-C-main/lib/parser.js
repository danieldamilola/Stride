/**
 * @file parser.js
 * @description Extracts, validates, and normalizes AI analysis responses.
 * @module lib/parser
 */

const VALID_IMPORTANCES = ["high", "medium", "low"];

/**
 * Parse a raw AI response string into a validated analysis object.
 *
 * @param {string} rawResponse - Raw text from the AI model.
 * @returns {Object} Validated analysis object.
 * @throws {Error} When the response cannot be parsed or validated.
 */
export function parse(rawResponse) {
  const json = extractJSON(rawResponse);

  if (json.is_terms === false) {
    return {
      is_terms: false,
      message:
        json.message || "This doesn't appear to be a Terms & Conditions page.",
      suggested_url: json.suggested_url || null,
    };
  }

  if (json.is_terms !== true) {
    throw new Error("PARSE_ERROR: Missing required field: is_terms");
  }

  return validateAnalysis(json);
}

/**
 * Check whether a raw response is a gatekeeper rejection.
 *
 * @param {string} rawResponse - Raw text from the AI model.
 * @returns {boolean} True when the response rejects the page as non-terms.
 */
export function isGatekeeperRejection(rawResponse) {
  try {
    const json = extractJSON(rawResponse);
    return json.is_terms === false;
  } catch {
    return false;
  }
}

function extractJSON(rawResponse) {
  if (typeof rawResponse !== "string") {
    throw new Error("PARSE_ERROR: AI response must be text.");
  }

  const text = rawResponse.trim();

  try {
    return JSON.parse(text);
  } catch {}

  const codeBlockMatch = text.match(/```(?:json)?\s*\n?([\s\S]*?)\n?```/i);
  if (codeBlockMatch) {
    try {
      return JSON.parse(codeBlockMatch[1].trim());
    } catch {}
  }

  const jsonMatch = text.match(/\{[\s\S]*\}/);
  if (jsonMatch) {
    try {
      return JSON.parse(jsonMatch[0]);
    } catch {}

    try {
      return JSON.parse(fixCommonJSONIssues(jsonMatch[0]));
    } catch {}
  }

  throw new Error(
    "PARSE_ERROR: Could not extract valid JSON from AI response.",
  );
}

function fixCommonJSONIssues(jsonString) {
  return jsonString
    .replace(/,\s*([}\]])/g, "$1")
    .replace(/'/g, '"')
    .replace(/\n/g, " ")
    .replace(/\s{2,}/g, " ")
    .replace(/([{,]\s*)([A-Za-z_][\w-]*)\s*:/g, '$1"$2":')
    .trim();
}

function validateAnalysis(json) {
  const riskScore = normalizeRiskScore(json.risk_score);
  const summary = normalizeRequiredString(json.summary, "summary");
  const findings = normalizeFindings(json.findings);

  return {
    is_terms: true,
    risk_score: riskScore,
    summary,
    findings,
  };
}

function normalizeRiskScore(value) {
  if (value === undefined || value === null || value === "") {
    throw new Error("PARSE_ERROR: Missing required field: risk_score");
  }

  const score = typeof value === "number" ? value : Number.parseInt(value, 10);

  if (Number.isNaN(score)) {
    throw new Error("PARSE_ERROR: risk_score must be a number.");
  }

  return Math.max(0, Math.min(100, score));
}

function normalizeRequiredString(value, fieldName) {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`PARSE_ERROR: Missing or invalid field: ${fieldName}`);
  }

  return value.trim();
}

function normalizeFindings(findings) {
  if (!Array.isArray(findings)) {
    throw new Error("PARSE_ERROR: Missing or invalid field: findings");
  }

  if (findings.length === 0) {
    throw new Error(
      "PARSE_ERROR: findings array is empty. Expected at least one finding.",
    );
  }

  return findings.map((finding, index) =>
    normalizeFinding(finding || {}, index),
  );
}

function normalizeFinding(finding, index) {
  const title =
    typeof finding.title === "string" && finding.title.trim()
      ? finding.title.trim()
      : `Finding ${index + 1}`;
  const importance = VALID_IMPORTANCES.includes(finding.importance)
    ? finding.importance
    : "medium";
  const description =
    typeof finding.description === "string" && finding.description.trim()
      ? finding.description.trim()
      : "No description provided.";
  const quote = typeof finding.quote === "string" ? finding.quote.trim() : "";

  return { title, importance, description, quote };
}
