# T&C Lens — Response Parser Design

## Role

The parser (`lib/parser.js`) takes the raw string response from the AI and converts it into a validated, structured JavaScript object. It handles format variations across AI providers and validates that all required fields are present before the UI tries to render them.

## Challenges

AI models are not perfectly predictable. Even with a well-crafted prompt, the response format can vary:

1. **Clean JSON** — Model returns pure JSON (ideal case)
2. **Markdown-wrapped JSON** — Model wraps the JSON in `json ... ` code blocks
3. **Text + JSON** — Model adds explanatory text before or after the JSON
4. **Malformed JSON** — Model produces slightly invalid JSON (trailing commas, unquoted keys)
5. **Missing fields** — Model omits required fields like `risk_score` or `findings`
6. **Wrong types** — Model returns `risk_score` as a string instead of a number

The parser handles all of these gracefully.

## Complete Implementation

````javascript
// lib/parser.js

/**
 * Parse a raw AI response string into a validated analysis object.
 *
 * @param {string} rawResponse - Raw text from the AI model
 * @returns {Object} Parsed and validated analysis
 * @throws {Error} If response cannot be parsed or is missing required fields
 */
export function parse(rawResponse) {
  // Step 1: Extract JSON from the response
  const json = extractJSON(rawResponse);

  // Step 2: Validate the gatekeeper field
  if (json.is_terms === false) {
    return {
      is_terms: false,
      message:
        json.message || "This doesn't appear to be a Terms & Conditions page.",
    };
  }

  // Step 3: Validate required fields for a successful analysis
  return validateAnalysis(json);
}

/**
 * Extract JSON object from various AI response formats.
 */
function extractJSON(rawResponse) {
  const text = rawResponse.trim();

  // Strategy 1: Direct JSON parse
  try {
    const parsed = JSON.parse(text);
    return parsed;
  } catch {}

  // Strategy 2: Extract from markdown code block
  const codeBlockMatch = text.match(/```(?:json)?\s*\n?([\s\S]*?)\n?```/);
  if (codeBlockMatch) {
    try {
      return JSON.parse(codeBlockMatch[1].trim());
    } catch {}
  }

  // Strategy 3: Find JSON object anywhere in the response
  const jsonMatch = text.match(/\{[\s\S]*\}/);
  if (jsonMatch) {
    try {
      return JSON.parse(jsonMatch[0]);
    } catch {
      // Try fixing common JSON issues before giving up
      try {
        const fixed = fixCommonJSONIssues(jsonMatch[0]);
        return JSON.parse(fixed);
      } catch {}
    }
  }

  throw new Error(
    "PARSE_ERROR: Could not extract valid JSON from AI response.",
  );
}

/**
 * Fix common JSON formatting issues from AI models.
 */
function fixCommonJSONIssues(jsonStr) {
  return jsonStr
    .replace(/,\s*([}\]])/g, "$1") // Remove trailing commas
    .replace(/'/g, '"') // Replace single quotes with double
    .replace(/\n/g, " ") // Remove newlines
    .replace(/\s{2,}/g, " ") // Collapse whitespace
    .replace(/(\w+)\s*:/g, '"$1":') // Quote unquoted keys (simple cases)
    .trim();
}

/**
 * Validate and normalize a successful analysis response.
 */
function validateAnalysis(json) {
  // Required fields
  if (!json.risk_score && json.risk_score !== 0) {
    throw new Error("PARSE_ERROR: Missing required field: risk_score");
  }

  if (typeof json.risk_score !== "number") {
    json.risk_score = parseInt(json.risk_score, 10);
    if (isNaN(json.risk_score)) {
      throw new Error("PARSE_ERROR: risk_score must be a number.");
    }
  }

  // Clamp score to 0-100
  json.risk_score = Math.max(0, Math.min(100, json.risk_score));

  if (!json.summary || typeof json.summary !== "string") {
    throw new Error("PARSE_ERROR: Missing or invalid field: summary");
  }

  if (!Array.isArray(json.findings)) {
    throw new Error("PARSE_ERROR: Missing or invalid field: findings");
  }

  if (json.findings.length === 0) {
    throw new Error(
      "PARSE_ERROR: findings array is empty. Expected at least one finding.",
    );
  }

  // Validate and normalize each finding
  const validImportances = ["high", "medium", "low"];

  const normalizedFindings = json.findings.map((finding, index) => {
    if (!finding.title || typeof finding.title !== "string") {
      finding.title = `Finding ${index + 1}`;
    }

    if (!validImportances.includes(finding.importance)) {
      // Default to medium if importance is invalid
      finding.importance = "medium";
    }

    if (!finding.description || typeof finding.description !== "string") {
      finding.description = "No description provided.";
    }

    // Quote is optional
    finding.quote = finding.quote || "";

    return {
      title: finding.title.trim(),
      importance: finding.importance,
      description: finding.description.trim(),
      quote: finding.quote.trim(),
    };
  });

  return {
    is_terms: true,
    risk_score: json.risk_score,
    summary: json.summary.trim(),
    findings: normalizedFindings,
  };
}

/**
 * Check if a parsed response is a gatekeeper rejection.
 * Useful for quick checking without full parse.
 */
export function isGatekeeperRejection(rawResponse) {
  try {
    const json = extractJSON(rawResponse);
    return json.is_terms === false;
  } catch {
    return false;
  }
}
````

## Validation Rules Summary

| Field                    | Required                 | Type    | Constraints           | Default on Missing         |
| ------------------------ | ------------------------ | ------- | --------------------- | -------------------------- |
| `is_terms`               | Yes                      | boolean | true or false         | N/A (throws error)         |
| `risk_score`             | Yes (when is_terms=true) | number  | 0-100                 | Throws error               |
| `summary`                | Yes (when is_terms=true) | string  | Non-empty             | Throws error               |
| `findings`               | Yes (when is_terms=true) | array   | At least 1 item       | Throws error               |
| `findings[].title`       | Yes                      | string  | Non-empty             | "Finding N"                |
| `findings[].importance`  | Yes                      | string  | "high"/"medium"/"low" | "medium"                   |
| `findings[].description` | Yes                      | string  | Non-empty             | "No description provided." |
| `findings[].quote`       | No                       | string  | Any                   | "" (empty string)          |

## Error Messages

When validation fails, the parser throws errors with a `PARSE_ERROR` prefix. The options page catches these and displays them:

| Validation Failure | Error Thrown                                      | User Message                                                                              |
| ------------------ | ------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| No JSON found      | `PARSE_ERROR: Could not extract valid JSON...`    | "The AI returned an unexpected response. Please try again."                               |
| Missing risk_score | `PARSE_ERROR: Missing required field: risk_score` | "The AI's response was incomplete. Please try again."                                     |
| Invalid risk_score | `PARSE_ERROR: risk_score must be a number`        | "The AI returned an invalid score. Please try again."                                     |
| Missing summary    | `PARSE_ERROR: Missing or invalid field: summary`  | "The AI's response was incomplete. Please try again."                                     |
| Empty findings     | `PARSE_ERROR: findings array is empty`            | "The AI found no issues but marked the page as a T&C. This is unusual. Please try again." |

## Testing the Parser

Unit testing approach (manual or with a test framework):

````javascript
// Test cases for the parser

// 1. Clean JSON
parse(
  '{"is_terms": true, "risk_score": 50, "summary": "Test", "findings": [...]}',
);
// → Should return valid analysis object

// 2. Markdown-wrapped JSON
parse('```json\n{"is_terms": true, "risk_score": 50, ...}\n```');
// → Should return valid analysis object

// 3. Text before JSON
parse('Here is my analysis:\n{"is_terms": true, ...}');
// → Should return valid analysis object

// 4. Gatekeeper rejection
parse('{"is_terms": false, "message": "Not a T&C page."}');
// → Should return { is_terms: false, message: "..." }

// 5. Missing risk_score
parse('{"is_terms": true, "summary": "Test", "findings": [...]}');
// → Should throw PARSE_ERROR

// 6. Invalid importance value
parse(
  '{"is_terms": true, "risk_score": 50, "summary": "Test", "findings": [{"title": "Test", "importance": "critical", "description": "Test"}]}',
);
// → Should default importance to "medium"

// 7. Score clamping
parse(
  '{"is_terms": true, "risk_score": 150, "summary": "Test", "findings": [...]}',
);
// → Should clamp to 100
````
