# T&C Lens — Contributor Guidelines

## Code Standards

This project is built with **vanilla JavaScript** (ES modules) — no frameworks, no build step, no transpilers. Every contributor must follow the standards below to keep the codebase clean, readable, and maintainable.

---

## 1. General Principles

- **Readability over cleverness.** If a one-liner requires a double-take, split it into multiple lines with clear variable names. A future contributor (or you in 3 months) should understand every line without guessing.
- **Self-documenting code first.** Code should explain _what_ it does through naming and structure. Comments explain _why_, not _what_.
- **No premature abstraction.** Don't create a helper function for something used once. Abstract when a pattern repeats 3+ times or when the logic is complex enough to benefit from a named function.
- **Fail visibly.** Never silently swallow errors. Every catch block must either handle the error meaningfully or re-throw with context.

---

## 2. File Organization

```
tc-lens/
├── manifest.json              # Extension config — only extension metadata
├── background.js               # Service worker — event listeners and message relay
├── content/
│   └── scraper.js              # Content script — DOM text extraction only
├── options/
│   ├── options.html            # Page structure — semantic HTML only
│   ├── options.css             # All styles — organized by component/view
│   └── options.js              # Orchestration — UI logic, event binding, rendering
├── lib/
│   ├── ai-client.js            # AI API wrapper — one file per concern
│   ├── storage.js              # Storage abstraction — chrome.storage wrapper
│   └── parser.js               # Response parsing — pure functions
└── icons/                      # Extension icons
```

**Rules:**

- One concern per file. `ai-client.js` handles AI communication, not rendering. `parser.js` handles parsing, not storage.
- No files over **300 lines**. If a file grows beyond that, it's a signal to split it.
- Shared utilities go in `lib/`. View-specific logic stays in `options/`.
- No circular dependencies. `lib/` files should not import from `options/`.

---

## 3. Naming Conventions

### Files and Folders

- **Lowercase, hyphen-separated:** `ai-client.js`, `options.css`
- **Folders lowercase:** `content/`, `options/`, `lib/`
- **No abbreviations unless universally understood:** `parser.js` not `prsr.js`, `storage.js` not `stg.js`

### Variables and Functions

```javascript
// Use camelCase for variables and functions
const riskScore = 67;
const apiKey = 'sk-...';
function extractPageText() { }
function buildUserMessage(text) { }

// Use UPPER_SNAKE_CASE for constants
const MAX_HISTORY_ENTRIES = 100;
const DEFAULT_SETTINGS = { ... };
const SYSTEM_PROMPT = '...';

// Use UPPER_SNAKE_CASE for error codes
const ERROR_CODES = {
  NO_API_KEY: 'NO_API_KEY',
  INVALID_KEY: 'INVALID_KEY',
  RATE_LIMITED: 'RATE_LIMITED',
};

// Boolean variables — start with is/has/should/can
const isTerms = true;
const hasApiKey = !!settings.apiKey;
const shouldRetry = attempts < MAX_RETRIES;
const canAnalyze = hasApiKey && targetTabExists;
```

### DOM Elements

```javascript
// Use descriptive names, prefix with the element type
const analyzeButton = document.getElementById("btn-analyze");
const settingsForm = document.querySelector(".settings-form");
const riskScoreHeader = document.getElementById("risk-score");
const findingsContainer = document.getElementById("findings");

// Never do this:
const btn = document.getElementById("btn"); // What button?
const el = document.querySelector(".card"); // What element?
const x = document.getElementById("score"); // Score of what?
```

### CSS Classes

```javascript
// BEM-style naming: block__element--modifier
.dashboard {}
.dashboard__target-page {}
.dashboard__target-page--active {}

.findings-card {}
.findings-card__title {}
.findings-card__description {}
.findings-card--high {}
.findings-card--medium {}
.findings-card--low {}

.btn {}
.btn--primary {}
.btn--danger {}
.btn--disabled {}
```

---

## 4. Code Structure

### Function Design

Every function should do **one thing**. If a function's name contains "and," it's doing too much.

```javascript
// BAD — does two things
function validateAndSaveSettings(form) {}

// GOOD — split into two
function validateSettings(form) {}
function saveSettings(settings) {}
```

**Function signature order:**

```javascript
/**
 * Build the user message for the AI prompt.
 *
 * @param {string} text - Extracted page text content
 * @param {Object} [options] - Optional configuration
 * @param {number} [options.maxChars=32000] - Maximum characters to include
 * @returns {string} Formatted user message ready for AI API call
 */
function buildUserMessage(text, options = {}) {
  const maxChars = options.maxChars || 32000;
  // ...
}
```

Every exported function must have a **JSDoc comment** with:

- One-line description of what it does
- `@param` for each parameter with type and description
- `@returns` with type and description
- `@throws` if it can throw errors

### Variable Declaration Order

Inside a function, declare variables in this order:

```javascript
async function analyzePage(tabId) {
  // 1. Constants and configuration
  const MAX_RETRIES = 3;
  const TIMEOUT_MS = 60000;

  // 2. Inputs and stored values
  const settings = await getSettings();
  const { targetTabId } = await chrome.storage.local.get("targetTabId");

  // 3. Derived/computed values
  const hasValidKey = settings.apiKey && settings.apiKey.length > 10;
  const canProceed = hasValidKey && targetTabId === tabId;

  // 4. Early returns for validation
  if (!canProceed) {
    throw new TCLError("INVALID_STATE", "Cannot analyze this page.");
  }

  // 5. Main logic
  const text = await extractText(tabId);
  const result = await callAI(text, settings);

  // 6. Return
  return result;
}
```

### Conditional Logic

```javascript
// Prefer early returns over deep nesting
// BAD
function processResponse(response) {
  if (response) {
    if (response.success) {
      if (response.data) {
        return response.data;
      }
    }
  }
  return null;
}

// GOOD
function processResponse(response) {
  if (!response) return null;
  if (!response.success) return null;
  if (!response.data) return null;

  return response.data;
}
```

### Error Handling

```javascript
// Always catch with specific handling, never empty catch blocks
// BAD
try {
  await riskyOperation();
} catch (e) {
  // silent failure — nobody knows what happened
}

// GOOD
try {
  await riskyOperation();
} catch (error) {
  if (error.code === "INVALID_KEY") {
    showUserMessage("Your API key was rejected. Check Settings.");
  } else if (error.code === "RATE_LIMITED") {
    showUserMessage("Rate limit reached. Wait a moment.");
  } else {
    // Unexpected error — log for debugging, show generic message to user
    console.error("[T&C Lens] Unexpected error:", error);
    showUserMessage("Something went wrong. Please try again.");
  }
}
```

---

## 5. Documentation Standards

### File Header

Every `.js` file starts with a header comment:

```javascript
/**
 * @file ai-client.js
 * @description Provider-agnostic AI API wrapper for T&C Lens.
 *              Handles communication with OpenAI, Anthropic, and Google Gemini.
 *              Normalizes responses and errors across providers.
 *
 * @module lib/ai-client
 */
```

### Function Documentation

```javascript
/**
 * Analyze extracted page text using the configured AI provider.
 *
 * Sends the page text to the selected AI model with the analysis prompt,
 * and returns the raw text response from the AI.
 *
 * @param {string} text - The extracted text content from the target page
 * @param {Object} settings - User's AI configuration
 * @param {string} settings.provider - AI provider identifier ('openai'|'anthropic'|'gemini')
 * @param {string} settings.apiKey - API key for the selected provider
 * @param {string} settings.model - Model identifier (e.g., 'gpt-4o-mini')
 * @returns {Promise<string>} Raw text response from the AI model
 *
 * @throws {TCLError} With code 'NO_API_KEY' if apiKey is empty
 * @throws {TCLError} With code 'INVALID_PROVIDER' if provider is unrecognized
 * @throws {TCLError} With code 'INVALID_KEY' if the API rejects the key
 * @throws {TCLError} With code 'RATE_LIMITED' if the API rate limit is hit
 * @throws {TCLError} With code 'NETWORK_ERROR' if the request fails
 * @throws {TCLError} With code 'TIMEOUT' if the request exceeds 60 seconds
 *
 * @example
 * const settings = await getSettings();
 * const rawResponse = await analyze(extractedText, settings);
 * const analysis = parser.parse(rawResponse);
 */
export async function analyze(text, settings) {}
```

### Inline Comments

Use inline comments to explain **why**, not **what**:

```javascript
// BAD — describes what the code does (obvious from reading it)
// Add 1 to the counter
counter++;

// BAD — restates the code in English
// Check if API key exists
if (settings.apiKey) { }

// GOOD — explains a non-obvious decision
// Truncate from the beginning because T&C pages put definitions first
// and the important clauses (liability, arbitration) come later
if (text.length > maxChars) {
  text = text.substring(text.length - maxChars);
}

// GOOD — explains context that isn't in the code
// Anthropic requires this header for browser-side requests.
// Without it, the request is rejected with a CORS error.
// See: https://docs.anthropic.com/en/api/cors
headers: {
  'anthropic-dangerous-direct-browser-access': 'true',
}
```

### CSS Comments

```css
/* ========================================
   Dashboard View
   ======================================== */

/* Target page confirmation card */
.target-page-card {
}

/* Status indicator showing API key state */
.status-card {
}

/* ========================================
   Analysis Results View
   ======================================== */

/* Risk score header — large number with color coding */
.risk-score-header {
}

/* Importance badges: high (red), medium (amber), low (green) */
.findings-card--high {
  border-left-color: var(--color-high);
}
.findings-card--medium {
  border-left-color: var(--color-medium);
}
.findings-card--low {
  border-left-color: var(--color-low);
}
```

---

## 6. HTML Standards

- **Semantic HTML always.** Use `<section>`, `<nav>`, `<main>`, `<header>`, `<footer>`, `<article>` — never a `<div>` soup.
- **Accessibility basics:** Every interactive element must be a real `<button>` or `<a>`, not a `<div>` with an onclick. Images get `alt` text. Color is never the only indicator (always pair color with text or icons).
- **No inline styles or scripts.** All CSS in `.css` files, all JS in `.js` files.
- **View toggling** uses CSS classes (`hidden`), not `display:none` set via JavaScript.

```html
<!-- BAD -->
<div onclick="analyze()" style="color: blue; cursor: pointer;">Analyze</div>

<!-- GOOD -->
<button id="btn-analyze" class="btn btn--primary">Analyze This Page</button>
```

---

## 7. Commit Messages

Use **conventional commits** format:

```
type(scope): short description

Optional longer body explaining context.

Example commits:

feat(ai-client): add Anthropic Claude support
fix(parser): handle trailing commas in AI JSON response
docs(readme): update installation instructions for Gemini
refactor(scraper): simplify extraction fallback chain
style(options): improve card spacing and typography
test(parser): add test cases for malformed JSON inputs
chore: update manifest version to 1.0.1
```

**Types:**
| Type | When to Use |
|------|-------------|
| `feat` | New feature or capability |
| `fix` | Bug fix |
| `docs` | Documentation only (README, comments, .md files) |
| `style` | CSS changes, formatting, no logic change |
| `refactor` | Code restructuring without behavior change |
| `test` | Adding or updating tests |
| `chore` | Maintenance (version bump, config, no code change) |

---

## 8. Pull Request Guidelines

Before submitting a PR, ensure:

1. **It works.** Test the change manually — load the extension, verify the feature/fix.
2. **No regressions.** Existing functionality still works. Run through the relevant tests from TESTING.md.
3. **No debug code.** No `console.log()`, no commented-out code, no `// TODO: remove this`.
4. **Documented.** New functions have JSDoc. New UI elements have ARIA labels. New CSS is commented by section.
5. **One change per PR.** Don't bundle a feature fix + a refactor + a style update into one PR. Split them.
6. **Descriptive title.** "Fix badge update" not "Update code" or "Changes."

### PR Template

```markdown
## What

Brief description of the change.

## Why

Context — what problem does this solve? Reference an issue if applicable.

## How

Approach taken. Mention any design decisions worth noting.

## Testing

What did you test? List the scenarios from TESTING.md that you verified.

## Screenshots

If UI changed, include before/after screenshots.
```

---

## 9. Code Review Checklist (For Reviewers)

When reviewing someone else's code:

- [ ] Can you understand what the code does without asking the author?
- [ ] Are all functions documented with JSDoc?
- [ ] Are variables named descriptively (no single letters, no abbreviations)?
- [ ] Is error handling present for all async operations?
- [ ] Are there no empty catch blocks?
- [ ] Does the HTML use semantic elements?
- [ ] Are all interactive elements accessible (real buttons, ARIA labels)?
- [ ] Is the CSS organized with section comments?
- [ ] Does the commit message follow conventional commit format?
- [ ] Is the change scoped — no unrelated modifications?

---

## 10. Project-Specific Rules

These are non-negotiable for T&C Lens specifically:

| Rule                                                         | Reason                                               |
| ------------------------------------------------------------ | ---------------------------------------------------- |
| **No background scanning**                                   | Extension must be passive until user clicks the icon |
| **No DOM modification by content script**                    | Content script reads only, never writes to the page  |
| **No data sent to any server except the user's AI provider** | Privacy-first design                                 |
| **No telemetry or analytics**                                | No tracking of any kind                              |
| **No external CDN dependencies**                             | All code is bundled; no `<script src="cdn...">`      |
| **No `eval()` or inline script execution**                   | Manifest V3 CSP forbids it                           |
| **API key never logged to console**                          | Security requirement                                 |
| **All AI responses validated before rendering**              | Prevents rendering untrusted content                 |

---

**These guidelines exist so anyone — whether it's you, a classmate, or a stranger on the internet — can open any file in this project and immediately understand what it does, why it does it that way, and how to change it safely.**
