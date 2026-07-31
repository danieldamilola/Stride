/**
 * @file background.js
 * @description MV3 service worker: tab deduplication, context menu,
 *              message relay, scraper injection, badge, storage migration.
 */

const EXTENSION_PAGE = "options/options.html";
const CONTEXT_MENU_ID = "tcl-analyze";

/* ─── Install / update ───────────────────────────────────────── */

chrome.runtime.onInstalled.addListener(async () => {
  await migrateStorage();
  registerContextMenu();
});

chrome.runtime.onStartup.addListener(() => {
  registerContextMenu();
});

function registerContextMenu() {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({
      id: CONTEXT_MENU_ID,
      title: "Analyze with T\u0026C Lens",
      contexts: ["page", "selection"],
    });
  });
}

/* ─── Toolbar icon click ─────────────────────────────────────── */

chrome.action.onClicked.addListener(async (tab) => {
  await chrome.storage.local.set({ targetTabId: tab.id });
  await openOrFocusDashboard();
});

/* ─── Context menu click ─────────────────────────────────────── */

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId !== CONTEXT_MENU_ID) return;
  await chrome.storage.local.set({ targetTabId: tab.id });
  await openOrFocusDashboard();
});

/* ─── Tab deduplication ──────────────────────────────────────── */

async function openOrFocusDashboard() {
  const extensionOrigin = chrome.runtime.getURL("");
  const pageUrl = chrome.runtime.getURL(EXTENSION_PAGE);

  // Find any existing T&C Lens tab
  const [existing] = await chrome.tabs.query({ url: `${extensionOrigin}*` });

  if (existing) {
    await chrome.tabs.update(existing.id, { active: true, url: pageUrl });
    await chrome.windows.update(existing.windowId, { focused: true });
  } else {
    await chrome.tabs.create({ url: pageUrl });
  }
}

/* ─── Message router ─────────────────────────────────────────── */

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  handleMessage(message)
    .then(sendResponse)
    .catch((error) => sendResponse({ success: false, error: error.message }));
  return true;
});

async function handleMessage(message) {
  if (!message?.type) return { success: false, error: "Missing message type." };

  switch (message.type) {
    case "INJECT_SCRAPER":
      return await injectScraper(message.tabId);
    case "EXTRACT_PAGE_TEXT":
      return await forwardToContentScript(message.tabId);
    case "SET_BADGE":
      return await setBadge(message.tabId, message.score);
    default:
      return { success: false, error: `Unknown message type: ${message.type}` };
  }
}

/* ─── Scraper injection ──────────────────────────────────────── */

async function injectScraper(tabId) {
  if (!Number.isInteger(tabId))
    return { success: false, error: "Missing tabId." };

  try {
    await chrome.scripting.executeScript({
      target: { tabId },
      files: ["content/scraper.js"],
    });
    return { success: true };
  } catch (error) {
    return { success: false, error: `Injection failed: ${error.message}` };
  }
}

async function forwardToContentScript(tabId) {
  if (!Number.isInteger(tabId))
    return { success: false, error: "Missing tabId." };

  try {
    return await chrome.tabs.sendMessage(tabId, { type: "EXTRACT_PAGE_TEXT" });
  } catch (error) {
    return { success: false, error: `Content script error: ${error.message}` };
  }
}

/* ─── Badge ──────────────────────────────────────────────────── */

async function setBadge(tabId, score) {
  if (!Number.isInteger(tabId))
    return { success: false, error: "Missing tabId." };

  const safeScore = Math.max(0, Math.min(100, Number(score) || 0));
  // Neutral monochrome palette to match the UI — only two badge colours
  const color = safeScore >= 70 ? "#e5e5e5" : "#555555";

  try {
    await chrome.action.setBadgeText({ text: String(safeScore), tabId });
    await chrome.action.setBadgeBackgroundColor({ color, tabId });
    return { success: true };
  } catch (error) {
    console.warn("[T&C Lens] Badge update failed:", error.message);
    return { success: false, error: error.message };
  }
}

/* ─── Storage migration ──────────────────────────────────────── */

async function migrateStorage() {
  const CURRENT_SCHEMA_VERSION = 1;
  const result = await chrome.storage.local.get(["schemaVersion", "settings"]);
  if ((result.schemaVersion || 0) >= CURRENT_SCHEMA_VERSION) return;

  await chrome.storage.local.set({
    settings: {
      apiKey: "",
      provider: "gemini",
      model: "gemini-2.5-flash",
      ...(result.settings || {}),
    },
    schemaVersion: CURRENT_SCHEMA_VERSION,
  });
}
