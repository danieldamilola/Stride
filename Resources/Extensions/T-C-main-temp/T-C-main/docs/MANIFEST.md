# T&C Lens — Manifest Configuration

## manifest.json Reference

The manifest file is the heart of any Chrome extension. For T&C Lens, we use **Manifest V3** — the current and required standard for Chrome extensions. Below is the complete manifest with detailed explanations for every field.

```json
{
  "manifest_version": 3,
  "name": "T&C Lens",
  "version": "1.0.0",
  "description": "AI-powered Terms & Conditions analyzer. Summarize and categorize legal agreements by importance.",
  "permissions": ["activeTab", "storage", "scripting"],
  "background": {
    "service_worker": "background.js"
  },
  "icons": {
    "16": "icons/icon16.png",
    "48": "icons/icon48.png",
    "128": "icons/icon128.png"
  },
  "action": {
    "default_icon": {
      "16": "icons/icon16.png",
      "48": "icons/icon48.png"
    },
    "default_title": "T&C Lens — Analyze this page"
  }
}
```

## Field-by-Field Breakdown

### `manifest_version: 3`

Declares that this extension uses Manifest V3. V2 is deprecated and Chrome no longer accepts V2 extensions in the Web Store. V3 introduces service workers (replacing background pages), stricter CSP, and new API patterns.

### `name: "T&C Lens"`

The display name shown in `chrome://extensions/`, the Chrome Web Store, and the toolbar tooltip. Keep it short and memorable. "T&C Lens" communicates the purpose clearly — it's a lens for looking at Terms & Conditions.

### `version: "1.0.0"`

Semantic versioning: `MAJOR.MINOR.PATCH`. For an initial release, `1.0.0` signals a stable version. Chrome Web Store requires version strings to be dot-separated numbers (1-4 digits each).

### `description`

Shown in the Chrome Web Store listing and `chrome://extensions/`. Keep it under 132 characters for Chrome Web Store display. Should clearly communicate what the extension does in one sentence.

### `permissions`

```json
"permissions": [
  "activeTab",
  "storage",
  "scripting"
]
```

This is the most critical section. Every permission is a trust signal — Chrome warns users about what the extension can access. We request only three permissions, the minimum needed:

| Permission  | Why We Need It                                                                | What It Grants                                                                                                                                                                                                                 |
| ----------- | ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `activeTab` | Access the current tab's URL and inject scripts when the user clicks the icon | Grants temporary access to the active tab's URL, title, and allows `chrome.scripting` on that tab. This permission is user-triggered — Chrome only grants it when the user clicks the extension icon. No access to other tabs. |
| `storage`   | Save API key, provider selection, analysis history                            | Access to `chrome.storage.local` for persistent key-value storage. Data is scoped to this extension — no other extension or website can access it.                                                                             |
| `scripting` | Inject the content scraper script on demand                                   | Allows calling `chrome.scripting.executeScript()` to inject JavaScript into a specific tab. Combined with `activeTab`, this only works on the tab the user was viewing when they clicked the icon.                             |

**Permissions we deliberately do NOT request:**

| Permission                         | Why We Avoid It                                                                                                                         |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| `tabs`                             | Would grant access to URL and title of ALL tabs at any time. `activeTab` gives us what we need for the current tab only.                |
| `<all_urls>` or `host_permissions` | Would allow injecting scripts on every website. We only need access when the user explicitly clicks the icon (`activeTab` covers this). |
| `background`                       | Manifest V3 service workers don't need this permission — they're declared in the `background` field.                                    |
| `cookies`                          | We don't need to read or set cookies.                                                                                                   |
| `webRequest`                       | We don't intercept or modify network requests.                                                                                          |

This minimal-permission approach reduces the Chrome permission warning users see during installation and demonstrates the privacy-first design philosophy.

### `background`

```json
"background": {
  "service_worker": "background.js"
}
```

Registers `background.js` as the extension's service worker. In Manifest V3, the background is a service worker (not a persistent background page). Key characteristics:

- **Starts on demand** — The service worker wakes up when an event occurs (like `chrome.action.onClicked` or a message) and terminates when idle (typically after 30 seconds of inactivity).
- **No DOM access** — Service workers cannot access the DOM. They communicate with content scripts and extension pages via messaging.
- **Stateless** — The service worker cannot hold in-memory state between activations. Everything must be stored in `chrome.storage` or passed via messages.

For T&C Lens, the service worker's job is simple: receive the toolbar click event, store the tab ID, open the options page, and relay messages. It does not need to stay alive for extended periods.

### `icons`

```json
"icons": {
  "16": "icons/icon16.png",
  "48": "icons/icon48.png",
  "128": "icons/icon128.png"
}
```

Icon files used in different contexts:

- **16x16** — Favicon and toolbar icon (small)
- **48x48** — Extensions management page (`chrome://extensions/`)
- **128x128** — Chrome Web Store listing and installation dialog

**Design notes for icons:**

- The icon should be recognizable even at 16x16 pixels. A magnifying glass over a document, or a shield/checkmark over a contract, works well at small sizes.
- Use PNG format with transparency.
- Avoid too much detail — at 16x16, only 2-3 colors are distinguishable.
- Consider the badge overlay: the importance score badge appears over the bottom-right corner of the icon, so keep that area relatively clear.

### `action`

```json
"action": {
  "default_icon": {
    "16": "icons/icon16.png",
    "48": "icons/icon48.png"
  },
  "default_title": "T&C Lens — Analyze this page"
}
```

The `chrome.action` API controls the toolbar button appearance and behavior.

**Note: There is NO `default_popup` field.** This is intentional. By omitting `default_popup`, clicking the toolbar icon fires the `chrome.action.onClicked` event in the background service worker instead of opening a popup. This is what allows us to open a full-page dashboard in a new tab.

| Field           | Purpose                                                                                                           |
| --------------- | ----------------------------------------------------------------------------------------------------------------- |
| `default_icon`  | Icon displayed in the toolbar. 16px for standard density, 48px for high-density displays.                         |
| `default_title` | Tooltip text shown when hovering over the toolbar icon. Keep it actionable — tell the user what clicking will do. |

**Why no `default_popup`:**

- A popup is limited to ~800x600px, cramped for displaying detailed analysis
- A popup disappears when the user clicks outside, losing context
- A full page in a new tab provides ample space for the dashboard, analysis results, and settings
- A popup would still need to communicate with the target tab through the background, so no complexity is saved

## Content Security Policy

Manifest V3 enforces a strict Content Security Policy by default. The extension cannot:

- Use `eval()`, `new Function()`, or inline script execution
- Load scripts from external CDN URLs (all scripts must be bundled)
- Make fetch/XHR requests to arbitrary URLs (only declared in `host_permissions`)

For T&C Lens, the AI API calls are made from the **options page** context. Since the options page is an extension page, it can use `fetch()` to any HTTPS endpoint without additional `host_permissions` — extension pages have broader network access than content scripts. This means we can call OpenAI, Anthropic, or Google APIs directly from `options.js` without declaring their domains in the manifest.

**Important:** If we were to make AI calls from the content script or service worker, we would need `host_permissions` for those API domains. By keeping AI calls in the options page, we avoid this requirement.

## What's NOT in the Manifest (and Why)

| Excluded Feature           | Why                                                                                                         |
| -------------------------- | ----------------------------------------------------------------------------------------------------------- |
| `content_scripts`          | We inject the scraper on-demand via `chrome.scripting.executeScript` instead of auto-injecting on all pages |
| `web_accessible_resources` | The extension doesn't expose any resources to web pages                                                     |
| `options_page`             | We use `chrome.tabs.create()` to open the options page, which works without declaring it in the manifest    |
| `commands`                 | No keyboard shortcuts needed for V1                                                                         |
| `sandbox`                  | No sandboxed pages needed                                                                                   |
| `externally_connectable`   | No external websites need to communicate with the extension                                                 |
| `chrome_url_overrides`     | Not replacing any Chrome pages                                                                              |
