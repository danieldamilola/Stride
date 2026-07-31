# T&C Lens — Testing Plan

## Overview

T&C Lens is a vanilla JavaScript Chrome extension with no build step or test framework. Testing is primarily **manual and integration-focused**, with some unit-testable components (parser, storage wrapper). This plan covers what to test, how to test it, and what to verify before release.

## Testing Layers

### Layer 1: Unit Tests (Optional — for parser and storage)

These components are pure functions with no Chrome API dependencies and can be tested in isolation.

**Parser tests (`lib/parser.js`):**

| Test Case             | Input                               | Expected Result                    |
| --------------------- | ----------------------------------- | ---------------------------------- |
| Clean JSON            | Valid analysis JSON                 | Returns normalized analysis object |
| Markdown-wrapped JSON | ``json\n{...}\n` ```                | Extracts and parses JSON           |
| Text + JSON mixed     | "Here is the analysis:\n{...}"      | Extracts JSON object from text     |
| Gatekeeper rejection  | `{is_terms: false, message: "..."}` | Returns `{is_terms: false}`        |
| Missing risk_score    | Analysis JSON without risk_score    | Throws PARSE_ERROR                 |
| Invalid importance    | `importance: "critical"`            | Defaults to "medium"               |
| Score out of range    | `risk_score: 150`                   | Clamps to 100                      |
| Empty findings        | `findings: []`                      | Throws PARSE_ERROR                 |
| Missing quote field   | Finding without quote               | Sets quote to ""                   |
| Invalid JSON          | "Not JSON at all"                   | Throws PARSE_ERROR                 |

**Running parser tests:** Create a `test-parser.html` file that loads `parser.js` and runs assertions, displaying results in the browser. No test framework needed — a simple script that logs pass/fail for each case.

### Layer 2: Integration Tests (Manual)

These test the extension's components working together. Performed by loading the unpacked extension in Chrome.

#### Test 1: Fresh Install Flow

1. Open `chrome://extensions/`
2. Enable Developer Mode
3. Click "Load unpacked" → select the extension folder
4. **Verify:** Extension appears in the list with correct name, version, description
5. **Verify:** T&C Lens icon appears in toolbar (may need to click puzzle piece → pin)
6. **Verify:** Clicking the icon opens a new tab with the options page
7. **Verify:** Options page shows "Set up your API key" message
8. **Verify:** Dashboard shows the URL of the tab you were on

#### Test 2: Settings Configuration

1. On the options page, navigate to Settings view
2. Select "OpenAI" from provider dropdown
3. Enter a valid OpenAI API key
4. Select "GPT-4o-mini" model
5. Click "Save Settings"
6. **Verify:** Success message appears
7. Refresh the options page
8. **Verify:** Settings are still populated (persistence test)
9. Switch to Settings view again
10. **Verify:** API key is masked, provider and model are correct

#### Test 3: Full Analysis — Happy Path

1. Navigate to `https://policies.google.com/privacy` in one tab
2. Click the T&C Lens toolbar icon
3. **Verify:** Options page opens, shows Google Privacy Policy URL
4. **Verify:** Status shows "API Key: Configured (OpenAI)"
5. Click "Analyze This Page"
6. **Verify:** Loading state shows "Extracting page content..."
7. **Verify:** Loading state changes to "Analyzing Terms & Conditions..."
8. **Verify:** After 5-20 seconds, Analysis Results view appears
9. **Verify:** Risk score is displayed with appropriate color
10. **Verify:** Summary paragraph is present and readable
11. **Verify:** At least one finding exists in each importance category
12. **Verify:** Each finding has a title, description, and importance badge
13. **Verify:** Clicking "Back" returns to dashboard
14. **Verify:** Toolbar badge shows the risk score number

#### Test 4: Gatekeeper — Non-T&C Page

1. Navigate to `https://example.com` (a simple non-T&C page)
2. Click the T&C Lens icon
3. Click "Analyze This Page"
4. **Verify:** Analysis runs but returns "This doesn't look like a T&C page"
5. **Verify:** No badge update (score remains as before)

#### Test 5: Edge Cases

| Scenario                   | Steps                                                                     | Expected Result                                    |
| -------------------------- | ------------------------------------------------------------------------- | -------------------------------------------------- |
| Tab closed before analysis | Click icon → close original tab → click Analyze                           | Error: "The page you selected has been closed"     |
| No API key                 | Clear API key in settings → try Analyze                                   | Error: "Please set your API key in Settings first" |
| Invalid API key            | Enter fake key "sk-fake123" → try Analyze                                 | Error: "Your API key was rejected"                 |
| Network offline            | Disable internet → try Analyze                                            | Error: "Could not reach the AI service"            |
| Very short page            | Navigate to a nearly empty page → Analyze                                 | AI gatekeeper rejects as not a T&C page            |
| Very long page             | Navigate to an extremely long T&C → Analyze                               | Text is truncated, analysis succeeds               |
| Multiple tabs              | Open 3 tabs → click icon on tab 2 → verify options page shows tab 2's URL | Correct URL displayed, analysis targets tab 2      |

#### Test 6: History Management

1. Analyze a T&C page (successful analysis)
2. Navigate to Settings → History section
3. **Verify:** The analysis appears in history with URL, score, and timestamp
4. Analyze a second page
5. **Verify:** Both entries appear in history (newest first)
6. Click on the first history entry
7. **Verify:** Analysis results view shows with cached data
8. Click "Clear History"
9. **Verify:** History is empty
10. **Verify:** Dashboard no longer shows "Last analyzed" info

#### Test 7: Provider Switching

1. Configure OpenAI with a valid key → analyze successfully
2. Switch to Anthropic in settings, enter Anthropic key
3. **Verify:** Save succeeds
4. Analyze the same page again
5. **Verify:** Analysis succeeds with Anthropic (results may differ slightly)
6. Switch to Gemini, enter Gemini key
7. **Verify:** Analysis succeeds

#### Test 8: Badge Behavior

1. Analyze a T&C page with score 75 (high risk)
2. **Verify:** Badge shows "75" in red
3. Analyze a page with score 30 (low risk)
4. **Verify:** Badge shows "30" in green
5. Analyze a page with score 50 (medium risk)
6. **Verify:** Badge shows "50" in amber
7. Navigate to a different tab
8. **Verify:** Badge on the new tab is empty (badge is per-tab)

### Layer 3: Cross-Browser Testing

While T&C Lens targets Chrome, Manifest V3 is supported by other Chromium-based browsers:

| Browser        | Test Priority | Notes                                                    |
| -------------- | ------------- | -------------------------------------------------------- |
| Google Chrome  | **Must pass** | Primary target                                           |
| Microsoft Edge | Recommended   | Uses same extension APIs                                 |
| Brave          | Recommended   | May have stricter privacy defaults                       |
| Firefox        | Low priority  | Firefox uses Manifest V2/V3 hybrid — may need adaptation |

### Layer 4: Performance Testing

| Metric                     | Target  | How to Measure                        |
| -------------------------- | ------- | ------------------------------------- |
| Options page load          | < 500ms | DevTools Performance tab              |
| Content script injection   | < 200ms | Console timing                        |
| Text extraction            | < 100ms | Console timing                        |
| AI API call (Gemini Flash) | 2-10s   | Network tab timing                    |
| AI API call (GPT-4o)       | 5-20s   | Network tab timing                    |
| History save               | < 50ms  | Console timing                        |
| Badge update               | < 100ms | Visual observation                    |
| Extension load time        | < 200ms | `chrome://extensions/` load indicator |

### Layer 5: Security Testing

| Check                                            | Verification Method                                                                   |
| ------------------------------------------------ | ------------------------------------------------------------------------------------- |
| API key not visible in network tab of target tab | Analyze a page → check Network tab on original tab — no API calls should appear there |
| API key encrypted on disk                        | Check Chrome profile → Local Extension Settings → verify data is not plain text       |
| No external requests except AI API               | Analyze with DevTools Network tab open → verify only AI API endpoint is called        |
| Content script doesn't modify DOM                | Inspect the target page after analysis → no elements added/modified                   |
| Extension doesn't run on pages unless activated  | Check background service worker status before clicking icon — should be idle          |

## Pre-Release Checklist

Before publishing or submitting:

- [ ] All happy-path tests pass (Test 1-3)
- [ ] All edge cases handled gracefully (Test 5)
- [ ] History save/load works (Test 6)
- [ ] At least 2 providers tested successfully (Test 7)
- [ ] Badge colors match risk levels (Test 8)
- [ ] No console errors during normal operation
- [ ] Icons are correct sizes and display properly
- [ ] Extension description is clear and under 132 characters
- [ ] Version number is correct in manifest.json
- [ ] No leftover console.log() debug statements in production code
- [ ] File structure matches the README listing
- [ ] All files referenced in manifest.json exist
- [ ] Storage migration code runs without errors on fresh install
