# T&C Lens — AI Prompt Design

## Overview

The AI prompt is the **core intelligence** of T&C Lens. It serves two critical functions: first, acting as a **gatekeeper** to verify the extracted text is actually a legal document, and second, performing the **structured analysis** that produces categorized findings with a risk score.

## Prompt Architecture

The prompt uses a **system/user message split** — a standard pattern across all major AI providers:

- **System message:** Contains the role definition, output format rules, analysis instructions, and gatekeeper logic
- **User message:** Contains the extracted page text

## System Prompt

```
You are T&C Lens, an expert legal document analyzer. Your job is to read Terms of Service, Privacy Policies, and other legal agreements, then break them down into clear, actionable insights for everyday users.

## CRITICAL FIRST STEP — GATEKEEPER CHECK

Before analyzing, check if the text is actually a legal agreement document.

The text IS a legal agreement if it contains:
- Terms of Service / Terms and Conditions / Terms of Use
- Privacy Policy / Privacy Notice / Data Policy
- User Agreement / License Agreement / Subscription Agreement
- Acceptable Use Policy
- Cookie Policy
- Disclaimer / Liability limitations
- Any legal document where users agree to terms

The text is NOT a legal agreement if it:
- Is a blog post, news article, product description, or marketing content
- Is a homepage or navigation page with no legal content
- Is a login/signup form
- Is primarily images, menus, or UI elements with minimal text
- Is a generic webpage unrelated to legal terms

If the text is NOT a legal agreement, respond with ONLY this JSON (no explanation):
{"is_terms": false, "message": "This doesn't appear to be a Terms of Service, Privacy Policy, or legal agreement page."}

## ANALYSIS FORMAT (only if is_terms is true)

If the text IS a legal agreement, respond with this JSON structure:

{
  "is_terms": true,
  "risk_score": <number 0-100>,
  "summary": "<2-3 sentence plain-language summary>",
  "findings": [
    {
      "title": "<short descriptive title>",
      "importance": "<high|medium|low>",
      "description": "<2-4 sentence explanation of what this clause means and why it matters>",
      "quote": "<relevant exact quote from the text, if available>"
    }
  ]
}

## SCORING GUIDELINES

The risk_score is a number from 0 to 100 representing how concerning the terms are for the user:

0-30 (Low Risk): Standard, balanced terms. User rights are reasonable, data practices are transparent, no surprising clauses.

31-60 (Medium Risk): Some concerning elements. Minor data sharing, one-sided modifications, limited liability, arbitration clauses, auto-renewal without clear notice.

61-100 (High Risk): Significantly problematic. Broad data harvesting, selling user data, no opt-out, binding arbitration with class-action waiver, unlimited liability waivers, surveillance-like tracking, content ownership claims over user data.

## IMPORTANCE CATEGORIES

HIGH importance: Issues that directly affect user rights, privacy, data, money, or legal protections.
- Data collection and tracking practices
- Privacy and surveillance concerns
- Binding arbitration / forced dispute resolution
- Limitation of liability (company not responsible for damages)
- Content ownership and licensing claims
- Automatic renewal and payment terms
- Account deletion and data portability

MEDIUM importance: Issues that are important but less directly impactful.
- Third-party data sharing
- Cookie usage and tracking
- Terms modification without explicit notice
- Account suspension/termination policies
- Geographic restrictions and governing law

LOW importance: Standard boilerplate that's generally expected and unremarkable.
- Age requirements (e.g., must be 13+)
- Account registration requirements
- General service description
- Contact information
- Standard disclaimers
- Link to other policies

## RULES

1. Respond in JSON only. No markdown, no code blocks, no explanations outside the JSON.
2. Be specific — reference actual clauses and quote relevant text.
3. Use plain, non-legal language that any user can understand.
4. Include 3-10 findings total, prioritizing the most important.
5. If a finding is very important, put it first (findings are ordered by importance).
6. Don't hallucinate clauses — only reference what's actually in the text.
7. If the text is very short or incomplete, note that in the summary.
8. Always include a quote when possible to back up each finding.
```

## User Message Template

```
Analyze the following text from a webpage. Extract all findings, assign importance levels, and calculate a risk score.

---
[EXTRACTED PAGE TEXT HERE]
---
```

## Text Preprocessing

Before sending text to the AI, the options page applies preprocessing:

```javascript
function prepareTextForAI(rawText, maxTokens = 8000) {
  // Rough estimate: 1 token ≈ 4 characters for English text
  const maxChars = maxTokens * 4;

  let text = rawText;

  // If text is too long, truncate from the beginning
  // (T&C pages often have the most important content in the middle/end)
  if (text.length > maxChars) {
    text = text.substring(text.length - maxChars);
    text =
      "[Note: Page text was truncated. Showing the last portion.]\n\n" + text;
  }

  return text;
}
```

**Why truncate from the end?** T&C pages typically start with definitions and general info and put the more specific, important clauses (data collection, liability, arbitration) later. Truncating the beginning preserves the most legally significant content.

**Token limits by model:**

| Provider  | Model             | Context Window | Effective Limit for T&C Lens          |
| --------- | ----------------- | -------------- | ------------------------------------- |
| OpenAI    | GPT-4o            | 128K tokens    | ~30,000 chars (after prompt overhead) |
| OpenAI    | GPT-4o-mini       | 128K tokens    | ~30,000 chars                         |
| Anthropic | Claude 3.5 Sonnet | 200K tokens    | ~50,000 chars                         |
| Anthropic | Claude 3 Haiku    | 200K tokens    | ~50,000 chars                         |
| Google    | Gemini 1.5 Flash  | 1M tokens      | ~200,000 chars                        |
| Google    | Gemini 1.5 Pro    | 2M tokens      | ~400,000 chars                        |

In practice, most T&C pages are 10,000-50,000 characters, so truncation is rarely needed. But we guard against extreme cases.

## Response Parsing Strategy

The AI may return the JSON in different ways depending on the provider:

1. **Clean JSON** — `{"is_terms": true, ...}` (ideal case)
2. **JSON wrapped in markdown** — `json\n{...}\n` (common with some models)
3. **JSON with leading/trailing text** — "Here's the analysis:\n{...}" (possible with less controllable models)

The parser (`lib/parser.js`) handles all three cases:

````javascript
function extractJSON(rawResponse) {
  // Try direct parse first
  try {
    return JSON.parse(rawResponse);
  } catch {}

  // Try extracting from markdown code block
  const codeBlockMatch = rawResponse.match(
    /```(?:json)?\s*\n?([\s\S]*?)\n?```/,
  );
  if (codeBlockMatch) {
    try {
      return JSON.parse(codeBlockMatch[1]);
    } catch {}
  }

  // Try finding JSON object in the response
  const jsonMatch = rawResponse.match(/\{[\s\S]*\}/);
  if (jsonMatch) {
    try {
      return JSON.parse(jsonMatch[0]);
    } catch {}
  }

  throw new Error("Could not parse AI response as JSON");
}
````

## AI Response Schema

### Gatekeeper Response (Not a T&C Page)

```json
{
  "is_terms": false,
  "message": "This doesn't appear to be a Terms of Service, Privacy Policy, or legal agreement page."
}
```

### Successful Analysis Response

```json
{
  "is_terms": true,
  "risk_score": 67,
  "summary": "This Terms of Service allows the company to collect browsing data, share it with third parties, and requires binding arbitration for disputes. Users cannot delete their accounts, and the company claims broad ownership over user-generated content.",
  "findings": [
    {
      "title": "Broad Data Collection",
      "importance": "high",
      "description": "The service collects browsing history, search queries, device information, and location data. This is more extensive than most services need, creating a detailed profile of user behavior.",
      "quote": "We collect information about your browsing activity, search queries, device type, operating system, and approximate location to improve our services."
    },
    {
      "title": "Binding Arbitration",
      "importance": "high",
      "description": "Any disputes must go through arbitration instead of court, and you waive the right to join class-action lawsuits. This limits your legal options if something goes wrong.",
      "quote": "You agree to resolve any disputes through binding arbitration and waive your right to participate in class-action lawsuits."
    },
    {
      "title": "Third-Party Data Sharing",
      "importance": "medium",
      "description": "Your data may be shared with affiliated companies and service providers for advertising purposes. There is no explicit opt-out mechanism mentioned.",
      "quote": "We may share aggregated, non-personally identifiable information with our partners for advertising purposes."
    },
    {
      "title": "No Account Deletion",
      "importance": "medium",
      "description": "There is no mention of how to delete your account or request data removal. Once you sign up, your data may be retained indefinitely.",
      "quote": ""
    },
    {
      "title": "Age Requirement",
      "importance": "low",
      "description": "Users must be at least 13 years old to use the service. This is standard for most online services.",
      "quote": "You must be at least 13 years of age to create an account and use our services."
    }
  ]
}
```

## Prompt Iteration Strategy

The prompt is designed to be **provider-agnostic** — it works with OpenAI, Anthropic, and Google models without modification. However, some models may respond differently:

- **GPT-4o-mini** — Tends to follow JSON format reliably. May occasionally add explanatory text before the JSON.
- **Claude 3.5 Sonnet** — Excellent at following instructions. Very reliable JSON output.
- **Gemini 1.5 Flash** — Fast and cheap. May sometimes wrap JSON in markdown code blocks.
- **Gemini 1.5 Pro** — Most expensive but most thorough analysis.

The parser is designed to handle variations in output format, so the prompt doesn't need per-provider customization.

## Future Prompt Enhancements (Not in V1)

- **Jurisdiction awareness** — Ask the user their country and adjust analysis for relevant laws (GDPR, CCPA, etc.)
- **Comparison mode** — Compare two versions of T&C to detect changes
- **Clause-level detail** — Break down every individual clause instead of summarizing into findings
- **Plain-language rewrite** — Offer a "rewrite in simple terms" option for the entire document
