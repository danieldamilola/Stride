/**
 * @file storage.js
 * @description chrome.storage.local wrapper for settings and analysis history.
 * @module lib/storage
 */

const DEFAULT_SETTINGS = {
  apiKey: "",
  provider: "gemini",
  model: "gemini-2.5-flash",
};

const MAX_HISTORY_ENTRIES = 100;

/**
 * Get current settings with defaults applied.
 *
 * @returns {Promise<Object>} Complete settings object.
 */
export async function getSettings() {
  const result = await chrome.storage.local.get("settings");
  const settings = result.settings || {};

  return { ...DEFAULT_SETTINGS, ...settings };
}

/**
 * Save partial settings and return the merged result.
 *
 * @param {Object} updates - Partial settings update.
 * @returns {Promise<Object>} Complete saved settings object.
 */
export async function saveSettings(updates) {
  const currentSettings = await getSettings();
  const settings = { ...currentSettings, ...updates };

  await chrome.storage.local.set({ settings });

  return settings;
}

/**
 * Get analysis history entries.
 *
 * @returns {Promise<Array>} Analysis history ordered newest first.
 */
export async function getHistory() {
  const result = await chrome.storage.local.get("analysisHistory");

  return Array.isArray(result.analysisHistory) ? result.analysisHistory : [];
}

/**
 * Save an analysis entry to history.
 *
 * @param {Object} analysis - Analysis entry to save.
 * @returns {Promise<Object>} Saved analysis entry with generated ID.
 */
export async function saveAnalysis(analysis) {
  const history = await getHistory();
  const entry = {
    ...analysis,
    id: `tc-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
  };

  history.unshift(entry);

  if (history.length > MAX_HISTORY_ENTRIES) {
    history.length = MAX_HISTORY_ENTRIES;
  }

  await chrome.storage.local.set({ analysisHistory: history });

  return entry;
}

/**
 * Get a saved analysis by ID.
 *
 * @param {string} id - Analysis entry ID.
 * @returns {Promise<Object|null>} Matching entry or null.
 */
export async function getAnalysis(id) {
  const history = await getHistory();

  return history.find((entry) => entry.id === id) || null;
}

/**
 * Find the most recent analysis for a URL.
 *
 * @param {string} url - Page URL.
 * @returns {Promise<Object|null>} Matching entry or null.
 */
export async function getLatestAnalysisForUrl(url) {
  const history = await getHistory();

  return history.find((entry) => entry.url === url) || null;
}

/**
 * Delete an analysis entry.
 *
 * @param {string} id - Analysis entry ID.
 * @returns {Promise<boolean>} True when an entry was deleted.
 */
export async function deleteAnalysis(id) {
  const history = await getHistory();
  const filteredHistory = history.filter((entry) => entry.id !== id);

  if (filteredHistory.length === history.length) return false;

  await chrome.storage.local.set({ analysisHistory: filteredHistory });

  return true;
}

/**
 * Clear all analysis history.
 *
 * @returns {Promise<void>}
 */
export async function clearHistory() {
  await chrome.storage.local.set({ analysisHistory: [] });
}

/**
 * Get storage usage statistics.
 *
 * @returns {Promise<Object>} Usage stats for analysis history.
 */
export async function getStorageStats() {
  const history = await getHistory();
  const totalBytes =
    await chrome.storage.local.getBytesInUse("analysisHistory");

  return {
    totalBytes,
    entryCount: history.length,
    oldestEntry: history.length > 0 ? history[history.length - 1] : null,
    newestEntry: history.length > 0 ? history[0] : null,
  };
}

export { DEFAULT_SETTINGS };
