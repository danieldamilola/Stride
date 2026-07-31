# T&C Lens — Project Timeline

## Overview

This timeline assumes a **solo developer** working on this as a school project, dedicating roughly 2-4 hours per day. The total estimated duration is **2 weeks (10-14 days)** from start to submission-ready.

## Phase 1: Foundation (Days 1-2)

### Day 1: Project Setup and Skeleton

| Task                                      | Duration | Deliverable                                                        |
| ----------------------------------------- | -------- | ------------------------------------------------------------------ |
| Set up project folder structure           | 30 min   | All directories and empty files created                            |
| Write `manifest.json`                     | 30 min   | Complete manifest with correct permissions                         |
| Create placeholder icons                  | 1 hour   | 16x16, 48x48, 128x128 PNG icons (can use simple design)            |
| Write `background.js` skeleton            | 1 hour   | Toolbar click handler + message relay                              |
| Verify extension loads in Chrome          | 30 min   | Extension appears in `chrome://extensions/`, icon shows in toolbar |
| **Test:** Click icon → options page opens | 15 min   | New tab opens with options.html                                    |

**Day 1 Total: ~4 hours**

### Day 2: Storage and Settings

| Task                                                 | Duration  | Deliverable                                                         |
| ---------------------------------------------------- | --------- | ------------------------------------------------------------------- |
| Write `lib/storage.js`                               | 1.5 hours | All storage functions (get/save settings, history CRUD)             |
| Write `options/options.html` skeleton                | 1 hour    | HTML structure with all three views (dashboard, analysis, settings) |
| Write `options/options.css` foundation               | 1.5 hours | Base layout, navigation, card styles, color variables               |
| Implement Settings view functionality                | 1 hour    | Provider dropdown, API key input, model selection, save button      |
| **Test:** Save settings → refresh → settings persist | 30 min    | Settings read/write verified                                        |

**Day 2 Total: ~5.5 hours**

## Phase 2: Core Functionality (Days 3-5)

### Day 3: Content Script and Text Extraction

| Task                                          | Duration | Deliverable                                          |
| --------------------------------------------- | -------- | ---------------------------------------------------- |
| Write `content/scraper.js`                    | 2 hours  | Full extraction logic with fallback chain            |
| Wire up scraper injection from background     | 1 hour   | INJECT_SCRAPER + EXTRACT_PAGE_TEXT message flow      |
| Wire up extraction from options page          | 1 hour   | Options page triggers injection, receives text       |
| **Test:** Extract text from 5 different pages | 1 hour   | Verify extraction quality on various page structures |

**Day 3 Total: ~5 hours**

### Day 4: AI Client

| Task                                          | Duration  | Deliverable                                   |
| --------------------------------------------- | --------- | --------------------------------------------- |
| Write `lib/ai-client.js` — OpenAI provider    | 1.5 hours | OpenAI API implementation with error handling |
| Write `lib/ai-client.js` — Anthropic provider | 1 hour    | Anthropic API implementation                  |
| Write `lib/ai-client.js` — Gemini provider    | 1 hour    | Gemini API implementation                     |
| Write the system prompt                       | 1 hour    | Full prompt from AI_PROMPT.md                 |
| **Test:** Send test prompt to each provider   | 1.5 hours | Verify all 3 providers return valid JSON      |

**Day 4 Total: ~6 hours**

### Day 5: Parser and End-to-End Flow

| Task                                                  | Duration  | Deliverable                                    |
| ----------------------------------------------------- | --------- | ---------------------------------------------- |
| Write `lib/parser.js`                                 | 1.5 hours | JSON extraction, validation, normalization     |
| Wire up full analysis flow in options.js              | 2 hours   | Extract → AI call → parse → render             |
| Implement Analysis Results view rendering             | 1.5 hours | Risk score, summary, findings cards            |
| Implement loading states and error UI                 | 1 hour    | Spinner, error banners, user-friendly messages |
| **Test:** Full end-to-end analysis on a real T&C page | 1 hour    | Complete flow from click to results            |

**Day 5 Total: ~7 hours**

## Phase 3: Polish (Days 6-8)

### Day 6: Badge, History, and Dashboard

| Task                                               | Duration  | Deliverable                                                       |
| -------------------------------------------------- | --------- | ----------------------------------------------------------------- |
| Implement badge update flow                        | 1 hour    | SET_BADGE message → background updates toolbar                    |
| Implement Dashboard view fully                     | 1 hour    | Target page card, status card, analyze button, last analysis info |
| Implement History list in Settings                 | 1.5 hours | Show past analyses, click to view, delete individual entries      |
| Implement "Clear History"                          | 30 min    | Button with confirmation                                          |
| Wire history click → show cached analysis          | 1 hour    | Re-render analysis from stored data                               |
| **Test:** History save, display, clear, re-analyze | 1 hour    | Full history workflow verified                                    |

**Day 6 Total: ~6 hours**

### Day 7: Visual Design and CSS Polish

| Task                                            | Duration | Deliverable                                         |
| ----------------------------------------------- | -------- | --------------------------------------------------- |
| Refine CSS for all views                        | 2 hours  | Consistent spacing, typography, colors, card styles |
| Add importance color coding (High/Medium/Low)   | 1 hour   | Red/Amber/Green badges and card accents             |
| Style the loading overlay and error states      | 1 hour   | Smooth animations, clear visual hierarchy           |
| Responsive adjustments                          | 1 hour   | Looks good at common tab widths                     |
| Icon refinement (if needed)                     | 1 hour   | Ensure icons look clean at all sizes                |
| **Test:** Visual review of all views and states | 1 hour   | Screenshot each view for comparison                 |

**Day 7 Total: ~7 hours**

### Day 8: Error Handling and Edge Cases

| Task                                                  | Duration | Deliverable                                       |
| ----------------------------------------------------- | -------- | ------------------------------------------------- |
| Test and fix: tab closed error                        | 1 hour   | Graceful message when target tab no longer exists |
| Test and fix: no API key error                        | 30 min   | Settings link highlighted, clear message          |
| Test and fix: gatekeeper rejection                    | 1 hour   | "Not a T&C page" message displays correctly       |
| Test and fix: network errors                          | 1 hour   | Offline mode, timeout handling                    |
| Test and fix: invalid API key                         | 30 min   | Correct error message for each provider           |
| Test and fix: very long pages                         | 1 hour   | Truncation works, analysis still succeeds         |
| Test and fix: empty pages                             | 30 min   | Meaningful error when no text can be extracted    |
| **Test:** Full edge-case pass (see TESTING.md Test 5) | 1 hour   | All edge cases verified                           |

**Day 8 Total: ~6.5 hours**

## Phase 4: Pre-Release (Days 9-10)

### Day 9: Cross-Provider Testing and Cleanup

| Task                                      | Duration | Deliverable                                  |
| ----------------------------------------- | -------- | -------------------------------------------- |
| Test with OpenAI (GPT-4o and GPT-4o-mini) | 1 hour   | Both models produce valid results            |
| Test with Anthropic (Claude 3.5 Sonnet)   | 1 hour   | Produces valid results                       |
| Test with Gemini (1.5 Flash and 1.5 Pro)  | 1 hour   | Both models produce valid results            |
| Remove all debug console.log()            | 30 min   | Clean production code                        |
| Final code review                         | 1 hour   | Check for bugs, unused code, inconsistencies |
| **Test:** Full regression test pass       | 1 hour   | All TESTSING.md tests pass                   |

**Day 9 Total: ~5.5 hours**

### Day 10: Documentation and Packaging

| Task                                  | Duration  | Deliverable                                      |
| ------------------------------------- | --------- | ------------------------------------------------ |
| Write final user-facing README.md     | 1.5 hours | Installation, usage, screenshots                 |
| Create LICENSE file                   | 15 min    | MIT License                                      |
| Create .gitignore                     | 15 min    | Standard ignores                                 |
| Take screenshots for store listing    | 30 min    | 5 screenshots at 1280x800                        |
| Package extension as .zip             | 15 min    | Correct file structure, no \_docs/               |
| Upload to GitHub repository           | 1 hour    | Repo created, files pushed, release tagged       |
| (Optional) Submit to Chrome Web Store | 30 min    | Listing created, screenshots uploaded, submitted |

**Day 10 Total: ~4 hours**

## Summary

| Phase                   | Days | Key Milestones                                                   |
| ----------------------- | ---- | ---------------------------------------------------------------- |
| **Phase 1: Foundation** | 1-2  | Extension loads, settings work, storage persists                 |
| **Phase 2: Core**       | 3-5  | Content extraction, AI calls, parsing, end-to-end flow works     |
| **Phase 3: Polish**     | 6-8  | Badge, history, visual design, error handling complete           |
| **Phase 4: Release**    | 9-10 | All providers tested, documentation done, packaged and published |

**Total estimated effort:** ~50-55 hours across 10 days

## Buffer Time

This timeline is tight but realistic for a focused developer. If things go slower:

- **Day 11:** Reserved for fixing bugs found during final testing
- **Day 12:** Reserved for school project-specific requirements (report writing, presentation preparation)
- **Day 13-14:** Final submission buffer

## Risk Factors and Mitigations

| Risk                                      | Likelihood | Impact | Mitigation                                                       |
| ----------------------------------------- | ---------- | ------ | ---------------------------------------------------------------- |
| AI prompt produces inconsistent results   | Medium     | Medium | Robust parser handles format variations; tune prompt iteratively |
| Content script fails on specific sites    | Low        | Low    | Fallback extraction chain; AI gatekeeper catches bad extractions |
| Chrome extension APIs behave unexpectedly | Low        | High   | Test early and often; refer to Chrome extension docs             |
| Scope creep (adding features)             | High       | Medium | Stick to V1 scope. Log feature ideas for V2.                     |
| Provider API changes                      | Low        | Low    | Provider configs are isolated in ai-client.js; easy to update    |
| School deadline pressure                  | Medium     | High   | Start with Phase 2 (core) to have a working prototype ASAP       |

## School Project Submission Recommendations

If the school requires a **report or documentation**, the `_docs/` folder already contains comprehensive planning documentation that can be adapted into a project report. Key sections to include:

1. **Problem statement:** Users don't read T&C pages, leading to uninformed consent
2. **Solution:** AI-powered T&C analyzer as a Chrome extension
3. **Architecture:** Three-context message-passing design (background, content, options)
4. **Implementation details:** One section per major component
5. **Testing:** Results from the testing plan
6. **Challenges:** Document real problems faced and how they were solved
7. **Future work:** The payment/backend system idea, multi-language support, etc.
