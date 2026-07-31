# T&C Lens — Background Service Worker

## Role

The background service worker (`background.js`) is the **message-passing middleman** of T&C Lens. It sits between the options page and the content script, relaying messages between them since they cannot communicate directly. It also handles toolbar icon clicks and badge updates.

## Lifecycle

Manifest V3 service workers are **event-driven and ephemeral**:

- They **wake up** when Chrome delivers an event (icon click, message received, etc.)
- They **terminate** after ~30 seconds of inactivity
- They **cannot** hold in-memory state between activations
- They **cannot** access the DOM

This means everything the background needs to know must come from the event payload or `chrome.storage`.

## Event Listeners

### 1. Toolbar Icon Click

```javascript
chrome.action.onClicked.addListener(async (tab) => {
  // Store which tab the user was viewing when they clicked the icon
  await chrome.storage.local.set({ targetTabId: tab.id });

  // Open the full-page dashboard in a new tab
  chrome.tabs.create({
    url: chrome.runtime.getURL("options/options.html"),
  });
});
```

**What happens:**

1. User clicks the T&C Lens icon in the toolbar
2. Chrome fires `onClicked` with the current tab object
3. We store `tab.id` in `chrome.storage.local` as `targetTabId`
4. We open `options/options.html` in a new tab

**Why store the tab ID:** The options page opens in a new tab and has no way to know which tab the user came from. By storing it in `chrome.storage.local`, the options page can retrieve it on load and use it to communicate with the correct content script.

**Edge case — multiple rapid clicks:** If the user clicks the icon multiple times quickly, multiple options tabs could open, all pointing to the same `targetTabId`. This is acceptable behavior — each tab will function independently. If this becomes a problem, we can add a check: before opening a new tab, see if an options tab already exists and focus it instead.

### 2. Message Handler (Main Relay)

```javascript
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  handleMessage(message, sender)
    .then(sendResponse)
    .catch((error) => {
      sendResponse({ error: error.message });
    });

  // Return true to indicate we'll call sendResponse asynchronously
  return true;
});

async function handleMessage(message, sender) {
  switch (message.type) {
    case "INJECT_SCRAPER":
      return await injectScraper(message.tabId);

    case "EXTRACT_PAGE_TEXT":
      return await forwardToContentScript(message.tabId, message);

    case "SET_BADGE":
      return await setBadge(message.tabId, message.score);

    default:
      return { error: "Unknown message type" };
  }
}
```

**Message types handled:**

| Message Type        | From         | Action                                                   |
| ------------------- | ------------ | -------------------------------------------------------- |
| `INJECT_SCRAPER`    | Options page | Injects `content/scraper.js` into the target tab         |
| `EXTRACT_PAGE_TEXT` | Options page | Forwards to content script in target tab to extract text |
| `SET_BADGE`         | Options page | Updates the toolbar badge with the analysis score        |

### 3. Script Injection Handler

```javascript
async function injectScraper(tabId) {
  try {
    await chrome.scripting.executeScript({
      target: { tabId },
      files: ["content/scraper.js"],
    });
    return { success: true };
  } catch (error) {
    return { error: `Failed to inject scraper: ${error.message}` };
  }
}
```

**What happens:**

1. The options page sends `{ type: 'INJECT_SCRAPER', tabId }`
2. The background injects `content/scraper.js` into the target tab
3. The content script registers its own `chrome.runtime.onMessage` listener
4. Once injected, the content script is ready to receive the `EXTRACT_PAGE_TEXT` message

**Error handling:** If injection fails (e.g., due to page CSP restrictions), the error is caught and returned to the options page. The options page then shows a user-friendly error message suggesting the user try a different approach.

### 4. Content Script Message Forwarder

```javascript
async function forwardToContentScript(tabId, message) {
  try {
    const response = await chrome.tabs.sendMessage(tabId, {
      type: "EXTRACT_PAGE_TEXT",
    });
    return response;
  } catch (error) {
    return { error: `Failed to reach content script: ${error.message}` };
  }
}
```

**What happens:**

1. After the scraper is injected, the options page requests text extraction
2. The background forwards `EXTRACT_PAGE_TEXT` to the content script in the target tab
3. The content script extracts the page text and responds
4. The background passes the response back to the options page

**Common failure:** "Could not establish connection. Receiving end does not exist." This happens when the content script hasn't been injected yet or the tab was closed. The error is caught and surfaced to the user.

### 5. Badge Update Handler

```javascript
async function setBadge(tabId, score) {
  try {
    // Determine badge color based on score
    let color;
    if (score >= 70) {
      color = "#DC2626"; // Red — high risk
    } else if (score >= 40) {
      color = "#F59E0B"; // Amber — medium risk
    } else {
      color = "#10B981"; // Green — low risk
    }

    await chrome.action.setBadgeText({
      text: String(score),
      tabId: tabId,
    });

    await chrome.action.setBadgeBackgroundColor({
      color: color,
      tabId: tabId,
    });

    return { success: true };
  } catch (error) {
    // Badge update failure is non-critical — don't block the user
    console.warn("Badge update failed:", error.message);
    return { error: error.message };
  }
}
```

**Badge scoring logic:**

| Score Range | Badge Color     | Meaning                                 |
| ----------- | --------------- | --------------------------------------- |
| 0-39        | Green (#10B981) | Low risk — T&C page is mostly benign    |
| 40-69       | Amber (#F59E0B) | Medium risk — some concerning clauses   |
| 70-100      | Red (#DC2626)   | High risk — significant red flags found |

**Why badge updates go through the background:** The `chrome.action.setBadgeText` and `setBadgeBackgroundColor` APIs can be called from the options page directly (it's an extension page with access to `chrome.action`). However, routing through the background keeps the badge logic centralized and consistent. If we later need to add tab-specific badge logic, it's already in the right place.

**Non-critical failure:** Badge update failures are logged but don't block the user flow. The analysis results are still shown in the options page even if the badge fails to update.

## Complete Code Structure

```javascript
// background.js — T&C Lens Service Worker

// Handle toolbar icon clicks
chrome.action.onClicked.addListener(async (tab) => {
  await chrome.storage.local.set({ targetTabId: tab.id });
  chrome.tabs.create({
    url: chrome.runtime.getURL("options/options.html"),
  });
});

// Handle messages from options page
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  handleMessage(message, sender)
    .then(sendResponse)
    .catch((error) => {
      sendResponse({ error: error.message });
    });
  return true; // Keep message channel open for async response
});

async function handleMessage(message, sender) {
  switch (message.type) {
    case "INJECT_SCRAPER":
      return await injectScraper(message.tabId);

    case "EXTRACT_PAGE_TEXT":
      return await forwardToContentScript(message.tabId);

    case "SET_BADGE":
      return await setBadge(message.tabId, message.score);

    default:
      return { error: `Unknown message type: ${message.type}` };
  }
}

async function injectScraper(tabId) {
  try {
    await chrome.scripting.executeScript({
      target: { tabId },
      files: ["content/scraper.js"],
    });
    return { success: true };
  } catch (error) {
    return { error: `Script injection failed: ${error.message}` };
  }
}

async function forwardToContentScript(tabId, message) {
  try {
    const response = await chrome.tabs.sendMessage(tabId, {
      type: "EXTRACT_PAGE_TEXT",
    });
    return response;
  } catch (error) {
    return { error: `Content script communication failed: ${error.message}` };
  }
}

async function setBadge(tabId, score) {
  try {
    const color = score >= 70 ? "#DC2626" : score >= 40 ? "#F59E0B" : "#10B981";

    await chrome.action.setBadgeText({ text: String(score), tabId });
    await chrome.action.setBadgeBackgroundColor({ color, tabId });
    return { success: true };
  } catch (error) {
    console.warn("Badge update failed:", error.message);
    return { error: error.message };
  }
}
```

## State Management

The background service worker is **stateless**. It does not hold any variables between activations. All shared state lives in `chrome.storage.local`:

| Key           | Set By                        | Read By    | Purpose                         |
| ------------- | ----------------------------- | ---------- | ------------------------------- |
| `targetTabId` | background.js (on icon click) | options.js | Identifies which tab to analyze |

This is the only piece of state the background writes. All other state (API key, provider, analysis history) is managed by the options page and `lib/storage.js`.

## Testing the Background Script

Manual testing steps:

1. Load the unpacked extension in Chrome
2. Open any web page
3. Click the T&C Lens toolbar icon
4. **Verify:** A new tab opens with `options.html`
5. **Verify:** The options page shows the URL of the original tab
6. Open DevTools in the original tab → Console
7. **Verify:** No content script is injected (check for extension-specific logs)
8. Go back to the options tab and click "Analyze"
9. **Verify:** DevTools Console in the original tab shows the content script logging text extraction
10. After analysis completes, **verify:** the toolbar icon shows a badge with the score and appropriate color
