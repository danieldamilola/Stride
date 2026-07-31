# T&C Lens — Architecture

## System Overview

T&C Lens follows a **message-passing architecture** with three isolated execution contexts communicating through Chrome's messaging APIs. The background service worker acts as the central coordinator, the content script handles DOM access on the target page, and the options page serves as the main user interface.

```
┌─────────────────┐         ┌──────────────────┐         ┌─────────────────────┐
│   Toolbar Icon  │  click  │  Background.js   │ stores  │  chrome.storage.local│
│  (chrome.action)│────────>│  (Service Worker) │────────>│  - targetTabId       │
└─────────────────┘         │                   │         │  - apiKey            │
                            │  Opens new tab:   │         │  - provider          │
                            │  options.html     │         │  - analysisHistory[] │
                            └────────┬──────────┘         └─────────────────────┘
                                     │
                                     ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                         OPTIONS PAGE (options.html)                          │
│                                                                              │
│  1. Reads targetTabId from storage                                          │
│  2. Sends message to background → background forwards to content script      │
│  3. Content script extracts text, sends back                                  │
│  4. Options page sends text to AI via ai-client.js                          │
│  5. AI returns categorized JSON                                              │
│  6. parser.js validates and structures the response                          │
│  7. Results rendered in the dashboard UI                                    │
│  8. Badge score updated via background message                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

## Execution Contexts

### Context 1: Background Service Worker (`background.js`)

**What it runs in:** An isolated service worker that starts on demand and terminates when idle. It has no DOM access and cannot directly interact with web pages.

**Responsibilities:**

- Listen for `chrome.action.onClicked` events (toolbar icon clicks)
- Store the active tab's ID into `chrome.storage.local`
- Open the options page in a new tab
- Act as a message relay between the options page and the content script (since they cannot message each other directly)
- Listen for badge update requests from the options page and call `chrome.action.setBadgeText` / `setBadgeBackgroundColor`

**Why we use a middleman:** The options page runs in its own tab with its own origin (`chrome-extension://...`). The content script runs in the context of the target web page. While MV3 does allow extension pages to message content scripts via `chrome.tabs.sendMessage()`, routing through the background is more robust because it also handles on-demand script injection via `chrome.scripting.executeScript()`. The background coordinates both injection and message relay in a single flow.

### Context 2: Content Script (`content/scraper.js`)

**What it runs in:** An isolated world within the target web page's tab. It has DOM access but shares no JavaScript scope with the page's own scripts.

**Responsibilities:**

- Receive `EXTRACT_PAGE_TEXT` message from the background
- Extract visible text content from the page using a smart fallback strategy
- Send the extracted text back to the background via message response

**Injection method:** This script is NOT declared in `manifest.json` and does NOT auto-inject. It is injected on-demand via `chrome.scripting.executeScript` only when the user clicks the toolbar icon and the options page requests analysis. This keeps the extension completely passive on pages the user isn't analyzing.

### Context 3: Options Page (`options/options.html` + `options/options.js`)

**What it runs in:** A full HTML page in a new tab with the extension's origin. This is the main UI — the "brain" of the extension where all orchestration happens.

**Responsibilities:**

- Display the dashboard with three views: Dashboard, Analysis Detail, and Settings
- Read stored configuration (API key, provider) and display current status
- Orchestrate the analysis flow: trigger content script extraction → send to AI → parse → render
- Handle error states (no API key, tab closed, invalid response, network errors)
- Save analysis results to history
- Provide settings UI for API key input, provider selection, and history management

## Data Flow — Step by Step

### Step 1: User Activation

```
User clicks toolbar icon
    → chrome.action.onClicked fires in background.js
    → background reads tab.id from the event
    → background stores { targetTabId: tab.id } in chrome.storage.local
    → background opens chrome.runtime.getURL('options/options.html') in a new tab
```

### Step 2: Options Page Loads

```
Options page opens
    → options.js runs on DOMContentLoaded
    → Reads settings from storage (apiKey, provider)
    → Checks if targetTabId exists in storage
    → Displays the target page URL as confirmation
    → Shows dashboard with "Analyze" button
```

### Step 3: User Clicks Analyze

```
User clicks "Analyze This Page"
    → options.js reads targetTabId from storage
    → options.js calls chrome.tabs.get(targetTabId) to verify tab still exists
    → If tab doesn't exist → show error "The page you selected has been closed"
    → If tab exists → proceed to Step 4
```

### Step 4: Content Extraction

```
options.js sends message to background: { type: 'INJECT_SCRAPER', tabId }
    → background receives message
    → background calls chrome.scripting.executeScript({
        target: { tabId },
        files: ['content/scraper.js']
      })
    → background sends message to content script: { type: 'EXTRACT_PAGE_TEXT' }
    → content script extracts text from page DOM
    → content script responds with { text: "...extracted content..." }
    → background forwards response back to options page
```

### Step 5: AI Analysis

```
options.js receives extracted text
    → Trims text to fit within AI model's context window (see AI_PROMPT.md)
    → Builds the analysis prompt (system prompt + user text)
    → Calls ai-client.js with provider, apiKey, model, and prompt
    → ai-client.js makes HTTP request to the selected AI provider's API
    → AI returns a JSON response string
```

### Step 6: Parsing & Rendering

```
options.js receives raw AI response
    → Passes response to parser.js
    → parser.js validates JSON structure and checks for is_terms flag
    → If is_terms is false → show "This doesn't look like a T&C page" message
    → If is_terms is true → extract summary, findings[], risk_score
    → Render results in the Analysis Detail view
    → Calculate badge score from risk_score
    → Send { type: 'SET_BADGE', score, tabId } to background
    → background calls chrome.action.setBadgeText and setBadgeBackgroundColor
    → Save analysis to history in chrome.storage.local
```

## Error Handling Strategy

Every step has explicit error handling. The extension never silently fails — each failure produces a clear, actionable message to the user.

| Step       | Possible Errors                      | User Message                                                                                                |
| ---------- | ------------------------------------ | ----------------------------------------------------------------------------------------------------------- |
| Activation | None (always succeeds)               | —                                                                                                           |
| Page Load  | Storage read failure                 | "Settings could not be loaded. Please refresh."                                                             |
| Analyze    | Tab was closed                       | "The page you selected has been closed. Please go back to the T&C page and click the extension icon again." |
| Analyze    | No API key set                       | "Please add your API key in Settings first."                                                                |
| Inject     | Script injection fails (CSP on page) | "This page blocks external scripts. Try copying the text manually."                                         |
| Extract    | Page has no usable text content      | "Could not extract text from this page. The page may be empty or use unsupported formatting."               |
| AI Call    | Network error                        | "Could not reach the AI service. Check your internet connection."                                           |
| AI Call    | Invalid API key                      | "Your API key was rejected. Please check your key in Settings."                                             |
| AI Call    | Rate limit / quota exceeded          | "API rate limit reached. Please wait a moment and try again."                                               |
| AI Call    | Response too large / timeout         | "The page content is too long for analysis. The AI took too long to respond."                               |
| Parse      | Invalid JSON from AI                 | "The AI returned an unexpected response. Please try again."                                                 |
| Parse      | Missing required fields              | "The AI response was incomplete. Please try again."                                                         |

## Security Considerations

1. **API Key Storage** — Stored in `chrome.storage.local`, which is accessible only to the extension itself. Not sent to any server except the user's chosen AI provider.
2. **Content Security Policy** — The extension declares a strict CSP in `manifest.json` that restricts `connect-src` to only the AI provider API endpoints. No inline scripts, no `eval()`.
3. **No DOM Injection** — The content script reads text but never modifies the page DOM. This prevents interference with the target page's behavior.
4. **No Data Collection** — The extension does not phone home. There is no analytics, no telemetry, no usage tracking. All data stays local in the user's browser.
5. **Minimal Permissions** — `activeTab` grants access only to the current tab when the user clicks the icon. `storage` is needed for settings. `scripting` is needed for on-demand content script injection. No broad host permissions.

## Future Architecture Considerations (Not in V1)

These are noted for potential future development but are **not part of the initial build**:

- **Proxy Backend** — A server-side proxy to hold an API key for users who don't want to manage their own. Would require auth, rate limiting, and potentially payments.
- **Multi-language Support** — Analyzing T&C pages in languages other than English. Would require prompt adjustments and model selection.
- **Comparison Feature** — Compare T&C text across versions to detect changed clauses. Would require storing historical page text.
- **Export/Share** — Allow users to export analysis as PDF or share a link. Would require generating documents or a backend.
