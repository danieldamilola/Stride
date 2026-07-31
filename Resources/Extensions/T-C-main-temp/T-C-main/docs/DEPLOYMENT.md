# T&C Lens — Deployment & Publishing

## Overview

This guide covers preparing T&C Lens for distribution via the Chrome Web Store and as a downloadable open-source package on GitHub.

## Pre-Publish Checklist

### Code Preparation

1. **Remove all development artifacts:**
   - Delete `console.log()` statements (keep `console.warn` and `console.error` for error reporting)
   - Remove any test files (`test-parser.html`, etc.) from the distribution package
   - Remove the `_docs/` folder from the distribution package (keep it in the GitHub repo only)

2. **Verify all files:**
   - All files listed in `manifest.json` exist at the correct paths
   - No orphaned files that aren't referenced
   - All icons are present at the correct sizes (16x16, 48x48, 128x128)

3. **Version check:**
   - `manifest.json` version matches the release version
   - If updating, increment version number per semantic versioning

4. **Final manual test:**
   - Load the extension fresh (remove old version first)
   - Run through the full happy-path test (see TESTING.md)
   - Verify on at least one non-T&C page (gatekeeper test)

### Required Assets for Chrome Web Store

| Asset                    | Spec                    | Purpose                          |
| ------------------------ | ----------------------- | -------------------------------- |
| Extension icon (128x128) | 128x128 PNG             | Store listing and install dialog |
| Extension icon (48x48)   | 48x48 PNG               | Chrome Web Store management page |
| Promotional tile (small) | 440x280 PNG             | Optional but recommended         |
| Promotional tile (large) | 920x680 PNG             | Optional                         |
| Detailed description     | Up to 10,000 chars      | Store listing body               |
| Short description        | Up to 132 chars         | Store listing summary            |
| Screenshots              | 1280x800 or 640x400 PNG | At least 1, up to 5              |

### Screenshots to Capture

1. **Dashboard view** — Showing the target page URL, status, and analyze button
2. **Analysis results** — Showing the risk score, summary, and categorized findings
3. **Settings page** — Showing the API key input, provider selection, and model picker
4. **Gatekeeper rejection** — Showing the "not a T&C page" message
5. **Badge display** — Showing the colored badge on the toolbar icon

Take screenshots at 1280x800 PNG for the best quality. Use a clean browser theme (default Chrome theme) without other extensions visible in the toolbar.

## Chrome Web Store Publishing Steps

### Step 1: Developer Account

1. Go to [Chrome Developer Dashboard](https://chrome.google.com/webstore/devconsole)
2. Sign in with a Google account
3. Pay the one-time $5 registration fee (required for all developers)
4. Complete the developer profile (name, contact email, website)

### Step 2: Create Listing

1. Click "New Item" on the dashboard
2. Upload the extension `.zip` file (NOT the source folder — zip it first)
3. Fill in the listing details:
   - **Name:** T&C Lens
   - **Summary:** AI-powered Terms & Conditions analyzer. Categorize legal agreements by importance.
   - **Description:** Detailed description covering features, how it works, supported providers, and privacy approach
   - **Category:** Productivity
   - **Language:** English
   - **Privacy:** Be transparent — "This extension does not collect any user data. All data is stored locally in the browser."

### Step 3: Privacy Practices Declaration

Chrome Web Store requires a privacy practices disclosure. For T&C Lens:

| Question                                               | Answer                                                                                                                               |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| Does this extension collect user data?                 | **No**                                                                                                                               |
| Does this extension use cookies?                       | **No**                                                                                                                               |
| Does this extension use Google APIs?                   | **Yes** — Only if the user selects Gemini as their provider. The extension does not use Google APIs on its own.                      |
| Does this extension handle personal or sensitive data? | **No** — The extension only sends page text to the user's chosen AI provider. It does not process, store, or transmit personal data. |
| Does this extension use third-party analytics?         | **No**                                                                                                                               |

### Step 4: Upload & Submit

1. Upload the `.zip` file
2. Add screenshots (minimum 1, recommend 3-5)
3. Add the 128x128 icon
4. Add promotional tiles (optional but recommended)
5. Review all information for accuracy
6. Click "Submit for Review"

### Step 5: Review Process

- **Initial review:** Usually 1-3 business days
- **Possible outcomes:** Approved, rejected with feedback, or flagged for additional review
- **Common rejection reasons:**
  - Insufficient description or screenshots
  - Missing or broken icons
  - Permission usage not explained
  - Extension crashes or errors during review testing
  - Misleading name or description

If rejected, address the feedback, fix the issue, increment the version number, and resubmit.

## GitHub Repository Setup

### Repository Structure

```
tc-lens/
├── manifest.json
├── background.js
├── content/
│   └── scraper.js
├── options/
│   ├── options.html
│   ├── options.css
│   └── options.js
├── lib/
│   ├── ai-client.js
│   ├── storage.js
│   └── parser.js
├── icons/
│   ├── icon16.png
│   ├── icon48.png
│   └── icon128.png
├── _docs/              # Planning documentation (keep in repo, exclude from .zip)
│   ├── README.md
│   ├── ARCHITECTURE.md
│   └── ... (all .md files)
├── .gitignore
├── LICENSE
└── README.md           # User-facing README (different from _docs/README.md)
```

### README.md (User-Facing)

The root `README.md` should be user-oriented:

1. Project name and one-line description
2. Screenshot of the extension in action
3. Features list
4. Installation instructions (for non-technical users)
5. Getting an API key (link to Gemini free tier first)
6. Supported AI providers
7. Privacy statement
8. Contributing guidelines
9. License

### .gitignore

```
.DS_Store
Thumbs.db
node_modules/
*_debug.js
*_test.js
*.zip
.crxc
.vscode/
.idea/
```

### LICENSE

Use MIT License (recommended for open source school projects). Create a `LICENSE` file with the standard MIT license text.

### Release Process

For each release:

1. Update `manifest.json` version number
2. Test the extension thoroughly
3. Create a Git tag: `git tag v1.0.0 && git push --tags`
4. Create a GitHub Release with the `.zip` attached
5. Update the Chrome Web Store listing (if published)
6. Update the root README.md if needed

## Distribution Without Chrome Web Store

Users can install the extension without the Chrome Web Store:

1. Download the `.zip` from GitHub Releases
2. Extract to a folder
3. Open `chrome://extensions/`
4. Enable Developer Mode
5. Click "Load unpacked" → select the extracted folder

This is called "side-loading." Chrome will show a warning that the extension isn't from the store, but it works fine. For a school project submission, this is usually sufficient.

## Post-Launch Considerations

### Handling User Reports

Common issues users may encounter:

| Issue                      | Likely Cause                      | Fix                                                 |
| -------------------------- | --------------------------------- | --------------------------------------------------- |
| "Extension doesn't work"   | No API key set                    | Improve onboarding UX, make settings more prominent |
| "Analysis always fails"    | Invalid API key or wrong provider | Add API key validation before analysis              |
| "Page says not a T&C page" | Gatekeeper rejecting valid pages  | Tune the prompt or add a "force analyze" option     |
| "Badge doesn't show score" | Badge update failing silently     | Add badge update error handling to UI               |
| "Analysis is slow"         | Large page + slow model           | Show progress indicator, suggest faster model       |

### Updating the Extension

When pushing updates:

1. Increment version in `manifest.json`
2. Test the update flow: existing users should get the new version automatically (if published on Chrome Web Store)
3. If side-loaded, users need to re-download and re-load the unpacked extension
4. Run storage migration code if the data model changed

### Analytics (Future — Not in V1)

If you want to understand how the extension is being used (after adding a backend):

- Track: number of analyses per day, most common providers, average page length
- Don't track: which URLs users analyze (privacy concern), API keys, personal data
- Use a privacy-respecting analytics approach (e.g., Plausible, Umami)
