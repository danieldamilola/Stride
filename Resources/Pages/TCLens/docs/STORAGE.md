# T&C Lens — Storage Design

## Role

The storage module (`lib/storage.js`) wraps `chrome.storage.local` to provide a clean API for persisting extension data. It handles serialization, default values, and data access patterns used throughout the extension.

## Why chrome.storage.local

Chrome extensions have several storage options:

| Option                   | Persistent? | Size Limit                                      | Use Case                                |
| ------------------------ | ----------- | ----------------------------------------------- | --------------------------------------- |
| `chrome.storage.local`   | Yes         | 10MB (can be increased with `unlimitedStorage`) | Persistent key-value data               |
| `chrome.storage.sync`    | Yes         | 100KB (total)                                   | Settings synced across devices          |
| `chrome.storage.session` | No          | 10MB                                            | Temporary data for current session      |
| `IndexedDB`              | Yes         | No hard limit                                   | Structured data, large datasets         |
| `localStorage`           | Yes         | 5MB                                             | Simple key-value (not extension-scoped) |

For T&C Lens, `chrome.storage.local` is the right choice because:

1. **Data persistence needed** — API key, provider, and analysis history should survive browser restarts
2. **Extension-scoped** — Data is only accessible to this extension
3. **Async API** — Non-blocking reads/writes (unlike `localStorage`)
4. **Simple key-value** — Our data structure is flat enough that we don't need IndexedDB's complexity
5. **10MB is plenty** — Even with hundreds of analysis results, we won't approach the limit

We do NOT use `chrome.storage.sync` because analysis history is device-specific and would eat through the 100KB sync quota quickly.

## Data Model

### Settings Object

```javascript
{
  apiKey: 'sk-abc123...',        // User's API key for the selected provider
  provider: 'openai',            // 'openai' | 'anthropic' | 'gemini'
  model: 'gpt-4o-mini',          // Selected model ID
}
```

**Stored under key:** `settings`

**Defaults:**

```javascript
const DEFAULT_SETTINGS = {
  apiKey: "",
  provider: "gemini", // Default to Gemini (free tier)
  model: "gemini-1.5-flash", // Default to cheapest model
};
```

### Analysis Entry

```javascript
{
  id: 'a1b2c3d4',                // Unique ID (timestamp-based)
  url: 'https://example.com/terms',
  title: 'Terms of Service — Example Corp',
  domain: 'example.com',
  analyzedAt: 1718123456789,      // Unix timestamp (ms)
  risk_score: 67,                 // 0-100
  summary: 'This T&C allows...', // AI-generated summary
  findings: [
    {
      title: 'Broad Data Collection',
      importance: 'high',
      description: 'The service collects...',
      quote: 'We collect information about...'
    }
  ]
}
```

**Stored under key:** `analysisHistory` (as an array of entries)

### Target Tab ID

```javascript
{
  targetTabId: 42; // The tab ID the user was viewing
}
```

**Stored under key:** `targetTabId`

This is written by `background.js` and read by `options.js`. It's ephemeral — only meaningful within the current user session.

## Storage Module API

```javascript
// lib/storage.js

const DEFAULT_SETTINGS = {
  apiKey: "",
  provider: "gemini",
  model: "gemini-1.5-flash",
};

const MAX_HISTORY_ENTRIES = 100; // Prevent unbounded growth

/**
 * Get current settings. Missing keys use defaults.
 * @returns {Promise<Object>} settings object
 */
export async function getSettings() {
  const result = await chrome.storage.local.get("settings");
  const settings = result.settings || {};
  return { ...DEFAULT_SETTINGS, ...settings };
}

/**
 * Save settings (merges with existing, applies defaults)
 * @param {Object} updates - Partial settings to update
 * @returns {Promise<Object>} complete settings after save
 */
export async function saveSettings(updates) {
  const current = await getSettings();
  const newSettings = { ...current, ...updates };
  await chrome.storage.local.set({ settings: newSettings });
  return newSettings;
}

/**
 * Get all analysis history
 * @returns {Promise<Array>} array of analysis entries
 */
export async function getHistory() {
  const result = await chrome.storage.local.get("analysisHistory");
  return result.analysisHistory || [];
}

/**
 * Add a new analysis to history
 * @param {Object} analysis - analysis entry to add
 * @returns {Promise<Object>} the saved entry
 */
export async function saveAnalysis(analysis) {
  const history = await getHistory();

  // Generate a unique ID
  const entry = {
    ...analysis,
    id: `tc-${Date.now()}-${Math.random().toString(36).substring(2, 6)}`,
  };

  // Add to front (newest first)
  history.unshift(entry);

  // Trim to max entries
  if (history.length > MAX_HISTORY_ENTRIES) {
    history.length = MAX_HISTORY_ENTRIES;
  }

  await chrome.storage.local.set({ analysisHistory: history });
  return entry;
}

/**
 * Get a specific analysis by ID
 * @param {string} id - analysis entry ID
 * @returns {Promise<Object|null>} the entry or null
 */
export async function getAnalysis(id) {
  const history = await getHistory();
  return history.find((entry) => entry.id === id) || null;
}

/**
 * Find the most recent analysis for a given URL
 * @param {string} url - page URL
 * @returns {Promise<Object|null>} the entry or null
 */
export async function getLatestAnalysisForUrl(url) {
  const history = await getHistory();
  return history.find((entry) => entry.url === url) || null;
}

/**
 * Delete a single analysis entry
 * @param {string} id - analysis entry ID
 * @returns {Promise<boolean>} true if deleted
 */
export async function deleteAnalysis(id) {
  const history = await getHistory();
  const filtered = history.filter((entry) => entry.id !== id);

  if (filtered.length === history.length) return false;

  await chrome.storage.local.set({ analysisHistory: filtered });
  return true;
}

/**
 * Clear all analysis history
 * @returns {Promise<void>}
 */
export async function clearHistory() {
  await chrome.storage.local.set({ analysisHistory: [] });
}

/**
 * Get storage usage stats
 * @returns {Promise<Object>} { totalBytes, entryCount, oldestEntry, newestEntry }
 */
export async function getStorageStats() {
  const history = await getHistory();
  const bytes = await chrome.storage.local.getBytesInUse("analysisHistory");

  return {
    totalBytes: bytes,
    entryCount: history.length,
    oldestEntry: history.length > 0 ? history[history.length - 1] : null,
    newestEntry: history.length > 0 ? history[0] : null,
  };
}
```

## Data Size Management

Each analysis entry is roughly 1-5KB depending on the number of findings and quote lengths. With the 100-entry cap:

- **Minimum:** ~100KB (100 entries × 1KB each)
- **Maximum:** ~500KB (100 entries × 5KB each)
- **Typical:** ~200KB (100 entries × 2KB each)

This is well within `chrome.storage.local`'s 10MB limit. If users need more history, we can either increase the cap or add the `unlimitedStorage` permission (which shows an additional warning during installation, so we avoid it unless necessary).

## Privacy and Security

### API Key Storage

The API key is stored in plain text in `chrome.storage.local`. This is the standard approach for browser extensions:

- `chrome.storage.local` is only accessible to the extension that wrote the data
- Other extensions and web pages cannot read it
- The data is stored in the user's Chrome profile directory (encrypted on disk if the user's OS supports it — Chrome uses the OS keychain on macOS and DPAPI on Windows)

**What we do NOT do:**

- Send the API key to any server other than the user's chosen AI provider
- Log the API key to the console
- Include the API key in any exported data

### Analysis History Privacy

Analysis history is stored locally and never sent anywhere. It includes the URL of analyzed pages, which some users may consider sensitive. The settings page includes a "Clear History" button for users who want to remove this data.

## Storage Events

`chrome.storage.onChanged` can be used to react to changes across contexts. For T&C Lens, this is useful for:

1. **Real-time settings sync** — If the user changes the API key in one tab, another options tab open simultaneously could detect the change
2. **History updates** — If the extension has multiple views open, they can refresh when new analyses are saved

```javascript
// Example: Listen for settings changes
chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local") {
    if (changes.settings) {
      console.log("Settings updated:", changes.settings.newValue);
    }
    if (changes.analysisHistory) {
      console.log(
        "History updated:",
        changes.analysisHistory.newValue?.length,
        "entries",
      );
    }
  }
});
```

For V1, this is optional — the extension only has one options tab open at a time. But the pattern is documented for future enhancement.

## Schema Versioning

If the data model changes in a future version (e.g., we add new fields to the analysis entry), we need migration logic:

```javascript
const CURRENT_VERSION = 1;

async function migrateStorage() {
  const result = await chrome.storage.local.get("schemaVersion");
  const version = result.schemaVersion || 0;

  if (version < 1) {
    // Migration to v1: Add default settings if they don't exist
    const settings = await getSettings();
    await chrome.storage.local.set({ settings, schemaVersion: 1 });
  }

  // Future migrations would go here
  // if (version < 2) { ... migrate to v2 ... }
}
```

This is called once on extension install or update via the `chrome.runtime.onInstalled` listener in `background.js`.

For V1, we start at version 1 with the current schema. The migration framework is included from the start to make future upgrades painless.
