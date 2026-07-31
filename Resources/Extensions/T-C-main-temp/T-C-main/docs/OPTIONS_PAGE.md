# T&C Lens — Options Page (Full-Page Dashboard)

## Role

The options page (`options/options.html` + `options/options.js`) is the **main user interface** of T&C Lens. It opens in a new tab when the user clicks the toolbar icon, and it serves as the control center for the entire extension. All orchestration — triggering content extraction, calling the AI, rendering results, and managing settings — happens here.

## Navigation Structure

The options page has **three views** the user can switch between using a sidebar or tab navigation:

```
┌─────────────────────────────────────────────────────────┐
│  T&C Lens                                    [Settings ⚙] │
├──────────┬──────────────────────────────────────────────┤
│          │                                              │
│ Dashboard│          [ Active View Content ]             │
│ Analysis │                                              │
│ History  │                                              │
│          │                                              │
│          │                                              │
│          │                                              │
└──────────┴──────────────────────────────────────────────┘
```

### View 1: Dashboard (Home)

The landing view shown when the options page first opens.

**Contents:**

- **Target page confirmation** — Shows the URL and title of the tab the user was viewing when they clicked the icon. This confirms "I'm analyzing the right page."
- **Status check** — Shows whether the API key is configured and which provider is selected. If no key is set, show a prominent call-to-action: "Set up your API key to get started."
- **Analyze button** — Large, prominent "Analyze This Page" button. Disabled if no API key is set.
- **Quick stats** — If there's a previous analysis for this URL, show a summary: "Last analyzed: 2 days ago — Risk Score: 45/100 (Medium)"

```
┌──────────────────────────────────────────────────┐
│  T&C Lens                                        │
│                                                  │
│  ┌────────────────────────────────────────────┐  │
│  │  🎯 Analyzing:                              │  │
│  │  https://example.com/terms-and-conditions    │  │
│  │  "Example Corp — Terms of Service"           │  │
│  └────────────────────────────────────────────┘  │
│                                                  │
│  ┌────────────────────────────────────────────┐  │
│  │  ⚙ API Key: Configured (OpenAI)             │  │
│  │  Model: GPT-4o-mini                          │  │
│  └────────────────────────────────────────────┘  │
│                                                  │
│  ┌────────────────────────────────────────────┐  │
│  │         [ 🔍 Analyze This Page ]            │  │
│  └────────────────────────────────────────────┘  │
│                                                  │
│  Last analyzed: Never                             │
└──────────────────────────────────────────────────┘
```

### View 2: Analysis Results

Shown after the AI completes its analysis. This is the core value-delivery view.

**Contents:**

- **Risk score header** — Large number (0-100) with color coding (green/amber/red) and a brief label ("Low Risk", "Medium Risk", "High Risk")
- **Summary paragraph** — A 2-3 sentence plain-language summary of the T&C document
- **Findings list** — Categorized cards/sections for High, Medium, and Low importance items. Each finding has:
  - Title (e.g., "Data Collection Practices")
  - Description (the AI's explanation)
  - Relevant clause quote (if available)
  - Importance badge (High/Medium/Low)
- **Re-analyze button** — Run the analysis again
- **Back to dashboard** link

```
┌──────────────────────────────────────────────────┐
│  Analysis Results          [← Back] [↻ Re-analyze]│
│                                                  │
│  ┌────────────────────────────────────────────┐  │
│  │           Risk Score: 67/100                │  │
│  │           ⚠ Medium Risk                    │  │
│  │                                             │  │
│  │  This service collects significant personal │  │
│  │  data including browsing history and device │  │
│  │  information. Some clauses limit user rights │  │
│  │  to arbitration.                             │  │
│  └────────────────────────────────────────────┘  │
│                                                  │
│  🔴 HIGH IMPORTANCE                              │
│  ┌────────────────────────────────────────────┐  │
│  │ Data Collection                              │  │
│  │ The service collects browsing history,      │  │
│  │ search queries, and device information...    │  │
│  │                                              │  │
│  │ 💬 "We may collect information about your    │  │
│  │ browsing activity across websites..."       │  │
│  └────────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────────┐  │
│  │ Binding Arbitration                          │  │
│  │ Disputes must be resolved through           │  │
│  │ arbitration, waiving your right to a        │  │
│  │ court trial...                               │  │
│  └────────────────────────────────────────────┘  │
│                                                  │
│  🟡 MEDIUM IMPORTANCE                            │
│  ┌────────────────────────────────────────────┐  │
│  │ Third-Party Sharing                          │  │
│  │ Data may be shared with affiliated          │  │
│  │ companies and service providers...          │  │
│  └────────────────────────────────────────────┘  │
│                                                  │
│  🟢 LOW IMPORTANCE                              │
│  ┌────────────────────────────────────────────┐  │
│  │ Account Termination                          │  │
│  │ The company may terminate accounts that      │  │
│  │ are inactive for 12+ months...               │  │
│  └────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────┘
```

### View 3: Settings

Where the user configures the extension.

**Contents:**

- **API Provider selection** — Dropdown: OpenAI / Anthropic / Google Gemini
- **API Key input** — Password-style input field (masked by default) with a show/hide toggle
- **Model selection** — Dropdown populated based on the selected provider (e.g., for OpenAI: GPT-4o, GPT-4o-mini)
- **Save button** — Persists settings to `chrome.storage.local`
- **Status indicator** — Shows "Saved" or "Error saving"
- **Analysis history** — List of past analyses with URL, date, and score. Click to view past results. Option to clear history.
- **About section** — Version, GitHub link, license info

```
┌──────────────────────────────────────────────────┐
│  Settings                                        │
│                                                  │
│  AI Provider                                     │
│  ┌────────────────────────────────────────────┐  │
│  │ [ OpenAI                    ▼ ]            │  │
│  └────────────────────────────────────────────┘  │
│                                                  │
│  API Key                                         │
│  ┌──────────────────────────────────┐ [👁 Show]  │
│  │ sk-****************************          │   │  │
│  └────────────────────────────────────────────┘  │
│  Get your key: platform.openai.com               │
│                                                  │
│  Model                                           │
│  ┌────────────────────────────────────────────┐  │
│  │ [ GPT-4o-mini                 ▼ ]          │  │
│  └────────────────────────────────────────────┘  │
│                                                  │
│  [  Save Settings  ]                             │
│                                                  │
│  ──────────────────────────────────────────────  │
│  Analysis History                                │
│  ┌────────────────────────────────────────────┐  │
│  │ example.com/terms  │ 67/100  │ 2 hours ago  │  │
│  │ github.com/privacy │ 23/100  │ Yesterday    │  │
│  └────────────────────────────────────────────┘  │
│  [ Clear History ]                               │
│                                                  │
│  ──────────────────────────────────────────────  │
│  T&C Lens v1.0.0 │ MIT License                   │
└──────────────────────────────────────────────────┘
```

## Analysis Flow (options.js)

### Step-by-Step Orchestration

```javascript
async function analyzeCurrentPage() {
  // 1. Validate prerequisites
  const settings = await storage.getSettings();
  if (!settings.apiKey) {
    showError("Please set your API key in Settings first.");
    return;
  }

  // 2. Get the target tab
  const { targetTabId } = await chrome.storage.local.get("targetTabId");
  let targetTab;
  try {
    targetTab = await chrome.tabs.get(targetTabId);
  } catch (error) {
    showError(
      "The page you selected has been closed. Go back to the T&C page and click the extension icon again.",
    );
    return;
  }

  // 3. Show loading state
  showLoading("Extracting page content...");

  // 4. Inject content script and extract text
  try {
    await chrome.runtime.sendMessage({
      type: "INJECT_SCRAPER",
      tabId: targetTabId,
    });
    const response = await chrome.runtime.sendMessage({
      type: "EXTRACT_PAGE_TEXT",
      tabId: targetTabId,
    });

    if (!response.text || response.text.length < 50) {
      showError(
        "Could not extract enough text from this page. It may be empty or dynamically loaded.",
      );
      return;
    }

    // 5. Send to AI
    showLoading("Analyzing Terms & Conditions...");
    const rawResponse = await aiClient.analyze(response.text, settings);
    hideLoading();

    // 6. Parse response
    const analysis = parser.parse(rawResponse);

    if (!analysis.is_terms) {
      showError(
        analysis.message || "This doesn't look like a Terms & Conditions page.",
      );
      return;
    }

    // 7. Render results
    renderAnalysis(analysis, targetTab.url);

    // 8. Update badge
    await chrome.runtime.sendMessage({
      type: "SET_BADGE",
      tabId: targetTabId,
      score: analysis.risk_score,
    });

    // 9. Save to history
    await storage.saveAnalysis({
      url: targetTab.url,
      title: targetTab.title,
      ...analysis,
      analyzedAt: Date.now(),
    });
  } catch (error) {
    hideLoading();
    handleAnalysisError(error);
  }
}
```

## Error Handling States

The UI should handle every failure gracefully with clear, actionable messages:

| State                      | UI Treatment                                                                         |
| -------------------------- | ------------------------------------------------------------------------------------ |
| No API key                 | Settings link highlighted with "Set up your API key" message                         |
| Tab closed                 | Warning banner with instructions to go back and re-click                             |
| Empty text extraction      | Error message suggesting the page may be empty or dynamic                            |
| Network error              | "Could not reach AI service. Check your internet connection."                        |
| Invalid API key            | "Your API key was rejected. Please check Settings."                                  |
| Rate limited               | "Rate limit reached. Wait a moment and try again."                                   |
| AI returned non-T&C        | "This doesn't look like a T&C page. The extension expected legal agreement content." |
| AI returned malformed JSON | "The AI returned an unexpected format. Please try again."                            |
| AI timeout                 | "The page is too long and the AI timed out. Try a shorter section."                  |

## Loading States

The analysis involves two network-dependent steps. The UI shows distinct loading messages:

1. **"Extracting page content..."** — While injecting the content script and receiving text
2. **"Analyzing Terms & Conditions..."** — While waiting for the AI to process and respond

A subtle loading animation (spinner or pulse) accompanies the text. The analyze button is disabled during loading.

## CSS and Theming

The options page uses a clean, modern design:

- **CSS Variables** for consistent theming — easy to add dark mode later
- **Responsive layout** — works well in various tab widths (most users don't resize their tabs, but it shouldn't break)
- **Color scheme:**
  - Background: White (#FFFFFF)
  - Text: Dark gray (#1F2937)
  - Primary accent: Blue (#3B82F6)
  - High risk: Red (#DC2626)
  - Medium risk: Amber (#F59E0B)
  - Low risk: Green (#10B981)
- **Typography:** System font stack for fast loading (`-apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif`)
- **No external dependencies** — Pure CSS, no frameworks. Keeps the extension lightweight and fast.

## File Structure

```
options/
├── options.html    # HTML structure with all three views
├── options.css     # All styles
└── options.js      # Orchestration logic, event handlers, rendering
```

**No build step.** All files are plain HTML/CSS/JS, loaded directly by the browser. This keeps the extension simple and easy to modify.

## HTML Structure (options.html skeleton)

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <title>T&C Lens</title>
    <link rel="stylesheet" href="options.css" />
  </head>
  <body>
    <!-- Navigation -->
    <nav>
      <span class="logo">T&C Lens</span>
      <div class="nav-tabs">
        <button data-view="dashboard" class="active">Dashboard</button>
        <button data-view="analysis">Analysis</button>
        <button data-view="settings">Settings</button>
      </div>
    </nav>

    <!-- Dashboard View -->
    <section id="view-dashboard">
      <div class="target-page-card">...</div>
      <div class="status-card">...</div>
      <button id="btn-analyze">Analyze This Page</button>
      <div class="quick-stats">...</div>
    </section>

    <!-- Analysis View (hidden initially) -->
    <section id="view-analysis" class="hidden">
      <div class="risk-score-header">...</div>
      <div class="summary">...</div>
      <div class="findings-container">...</div>
    </section>

    <!-- Settings View (hidden initially) -->
    <section id="view-settings" class="hidden">
      <div class="settings-form">...</div>
      <div class="history-container">...</div>
      <div class="about">...</div>
    </section>

    <!-- Loading Overlay -->
    <div id="loading-overlay" class="hidden">
      <div class="spinner"></div>
      <p id="loading-text">Extracting page content...</p>
    </div>

    <!-- Error Banner -->
    <div id="error-banner" class="hidden">
      <p id="error-text"></p>
      <button id="error-dismiss">Dismiss</button>
    </div>

    <script src="../lib/storage.js"></script>
    <script src="../lib/ai-client.js"></script>
    <script src="../lib/parser.js"></script>
    <script src="options.js"></script>
  </body>
</html>
```
