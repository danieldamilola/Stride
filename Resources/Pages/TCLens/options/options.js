/**
 * @file options.js
 * @description Dashboard orchestration for T&C Lens.
 *              Handles bootstrap, state, event wiring, view routing,
 *              settings form, and analysis flow coordination.
 */

import {
  analyze,
  estimateCost,
  getProviders,
  listAvailableModels,
  prepareTextForAI,
} from "../lib/ai-client.js";
import { parse } from "../lib/parser.js";
import {
  clearHistory,
  getSettings,
  saveAnalysis,
  saveSettings,
} from "../lib/storage.js";
import {
  escapeHTML,
  showMessage,
  showHtmlMessage,
  hideMessage,
  getUserMessage,
  getDomain,
  showConfirmModal,
} from "./utils.js";
import {
  renderDashboard,
  renderAnalysis,
  renderHistory,
} from "./renderer.js";
import { exportAnalysis } from "./export.js";

const providers = getProviders();

/** @type {{ settings: Object|null, targetTab: Object|null, targetTabId: number|null }} */
const state = {
  settings: null,
  targetTab: null,
  targetTabId: null,
};

/** @type {Object<string, HTMLElement>} */
const el = {};

/* ─── Bootstrap ──────────────────────────────────────────────── */

document.addEventListener("DOMContentLoaded", async () => {
  bindElements();
  bindEvents();
  populateProviderOptions();
  await loadInitialState();
});

/* ─── Element binding ────────────────────────────────────────── */

function bindElements() {
  el.views = Array.from(document.querySelectorAll(".view"));
  el.viewButtons = Array.from(document.querySelectorAll("[data-view-button]"));

  // Dashboard
  el.dashboardMessage = document.getElementById("dashboard-message");
  el.targetStatus = document.getElementById("target-status");
  el.targetTitle = document.getElementById("target-title");
  el.targetUrl = document.getElementById("target-url");
  el.apiStatus = document.getElementById("api-status");
  el.providerName = document.getElementById("provider-name");
  el.providerStatus = document.getElementById("provider-status");
  el.lastAnalysis = document.getElementById("last-analysis");
  el.analyzeButton = document.getElementById("btn-analyze");
  el.openSettingsBtn = document.getElementById("btn-open-settings");

  // Analysis
  el.backDashboardBtn = document.getElementById("btn-back-dashboard");
  el.reanalyzeBtn = document.getElementById("btn-reanalyze");
  el.analysisResults = document.getElementById("analysis-results");

  // Settings
  el.settingsForm = document.getElementById("settings-form");
  el.providerSelect = document.getElementById("provider-select");
  el.modelSelect = document.getElementById("model-select");
  el.refreshModelsBtn = document.getElementById("btn-refresh-models");
  el.apiKeyInput = document.getElementById("api-key-input");
  el.costEstimate = document.getElementById("cost-estimate");
  el.settingsMessage = document.getElementById("settings-message");
  el.historyList = document.getElementById("history-list");
  el.clearHistoryBtn = document.getElementById("btn-clear-history");

  // Coming soon / BYOK toggle
  el.comingSoonBanner = document.getElementById("coming-soon-banner");
  el.byokFields = document.getElementById("byok-fields");

  // Loading
  el.loadingOverlay = document.getElementById("loading-overlay");
  el.loadingMessage = document.getElementById("loading-message");
}

/* ─── Event wiring ───────────────────────────────────────────── */

function bindEvents() {
  for (const btn of el.viewButtons) {
    btn.addEventListener("click", () => showView(btn.dataset.viewButton));
  }

  el.openSettingsBtn.addEventListener("click", () => showView("settings-view"));
  el.backDashboardBtn.addEventListener("click", () =>
    showView("dashboard-view"),
  );
  el.reanalyzeBtn.addEventListener("click", () => {
    showView("dashboard-view");
    analyzeCurrentPage();
  });

  el.analyzeButton.addEventListener("click", analyzeCurrentPage);

  el.providerSelect.addEventListener("change", () => {
    handleProviderChange();
    updateCostEstimate();
  });
  el.modelSelect.addEventListener("change", updateCostEstimate);
  el.apiKeyInput.addEventListener("input", updateCostEstimate);

  el.refreshModelsBtn.addEventListener("click", refreshAvailableModels);
  el.settingsForm.addEventListener("submit", handleSettingsSubmit);
  el.clearHistoryBtn.addEventListener("click", handleClearHistory);
}

/* ─── Initial load ───────────────────────────────────────────── */

async function loadInitialState() {
  state.settings = await getSettings();
  applySettingsToForm(state.settings);
  state.settings = { ...state.settings, model: el.modelSelect.value };

  const stored = await chrome.storage.local.get("targetTabId");
  state.targetTabId = stored.targetTabId;
  state.targetTab = await getTargetTab(state.targetTabId);

  await renderDashboard(state, el, providers);
  updateCostEstimate();
  await renderHistory(el, openHistoryEntry);
}

/* ─── Provider / model selects ───────────────────────────────── */

function populateProviderOptions() {
  el.providerSelect.replaceChildren();
  for (const [id, p] of Object.entries(providers)) {
    const opt = document.createElement("option");
    opt.value = id;
    opt.textContent = p.comingSoon ? `${p.name} — Coming Soon` : p.name;
    el.providerSelect.appendChild(opt);
  }
}

function populateModelOptions(providerId, selectedModel) {
  const provider = providers[providerId] || providers.gemini;
  el.modelSelect.replaceChildren();

  for (const m of provider.models) {
    const opt = document.createElement("option");
    opt.value = m.id;
    opt.textContent = m.name;
    el.modelSelect.appendChild(opt);
  }

  const has = provider.models.some((m) => m.id === selectedModel);
  el.modelSelect.value = has ? selectedModel : provider.models[0].id;
}

function applySettingsToForm(settings) {
  el.providerSelect.value = settings.provider;
  handleProviderChange();
  populateModelOptions(settings.provider, settings.model);
  el.apiKeyInput.value = settings.apiKey || "";
}

function handleProviderChange() {
  const providerId = el.providerSelect.value;
  const provider = providers[providerId];

  // Toggle between coming-soon banner and BYOK fields
  const isComingSoon = Boolean(provider.comingSoon);
  el.comingSoonBanner.classList.toggle("hidden", !isComingSoon);
  el.byokFields.classList.toggle("hidden", isComingSoon);

  if (!isComingSoon) {
    populateModelOptions(providerId, provider.models[0].id);
  }
}

function updateCostEstimate() {
  const provider = el.providerSelect.value;
  const model = el.modelSelect.value;
  const hasKey = Boolean(el.apiKeyInput.value.trim());

  if (!hasKey) {
    el.costEstimate.textContent = "";
    return;
  }

  const cost = estimateCost("", provider, model);
  if (!cost) {
    el.costEstimate.textContent = "";
    return;
  }

  if (cost.totalCost === 0) {
    el.costEstimate.textContent = "free tier";
  } else {
    el.costEstimate.textContent = `~$${cost.totalCost.toFixed(4)} base cost`;
  }
}

/* ─── Model refresh ──────────────────────────────────────────── */

async function refreshAvailableModels() {
  const providerId = el.providerSelect.value;
  const apiKey = el.apiKeyInput.value.trim();

  if (!apiKey) {
    showMessage(
      el.settingsMessage,
      "Enter your API key before refreshing models.",
      "error",
    );
    return;
  }

  el.refreshModelsBtn.textContent = "Fetching…";
  el.refreshModelsBtn.disabled = true;

  try {
    const models = await listAvailableModels(providerId, apiKey);
    if (models.length === 0)
      throw new Error("No compatible models returned for this key.");

    providers[providerId].models = models;
    populateModelOptions(providerId, el.modelSelect.value);
    showMessage(
      el.settingsMessage,
      `${models.length} models loaded.`,
      "success",
    );
  } catch (error) {
    showMessage(el.settingsMessage, getUserMessage(error), "error");
  } finally {
    el.refreshModelsBtn.disabled = false;
    el.refreshModelsBtn.textContent = "Refresh available models";
  }
}

/* ─── Settings form ──────────────────────────────────────────── */

async function handleSettingsSubmit(event) {
  event.preventDefault();

  state.settings = await saveSettings({
    provider: el.providerSelect.value,
    model: el.modelSelect.value,
    apiKey: el.apiKeyInput.value.trim(),
  });

  showMessage(el.settingsMessage, "Saved.", "success");
  await renderDashboard(state, el, providers);
}

/* ─── Analysis flow ──────────────────────────────────────────── */

/**
 * Main orchestration function for analyzing a page.
 * 1. Validates API key and target tab.
 * 2. Injects scraper into the target tab and extracts text.
 * 3. Truncates text if necessary and calls the AI provider.
 * 4. Parses the response, saves to history, and updates the UI.
 */
async function analyzeCurrentPage() {
  hideMessage(el.dashboardMessage);

  if (!state.settings.apiKey?.trim()) {
    showMessage(
      el.dashboardMessage,
      "Add your API key in Settings first.",
      "error",
    );
    showView("settings-view");
    return;
  }

  state.targetTab = await getTargetTab(state.targetTabId);
  if (!state.targetTab) {
    renderDashboard(state, el, providers);
    showMessage(
      el.dashboardMessage,
      "That page has been closed. Re-open it and click the extension icon again.",
      "error",
    );
    return;
  }

  try {
    setLoading("Extracting page content…");

    await sendBg({ type: "INJECT_SCRAPER", tabId: state.targetTabId });
    const extraction = await sendBg({
      type: "EXTRACT_PAGE_TEXT",
      tabId: state.targetTabId,
    });
    const text = (extraction.text || "").trim();

    if (!text) throw new Error("Could not extract text from this page.");

    // Bypass truncation entirely for the hosted T&C Lens AI
    const limit = state.settings.provider === "tclens" ? Infinity : getModelLimit(state.settings);
    const prepared = prepareTextForAI(text, limit);

    setLoading("Analyzing with AI…");

    const raw = await analyze(prepared.text, state.settings, state.targetTab.url);
    const analysis = parse(raw);

    hideLoading();

    if (analysis.is_terms === false) {
      if (analysis.suggested_url) {
        showHtmlMessage(
          el.dashboardMessage,
          `${escapeHTML(analysis.message)}<br><br><span style="opacity:0.8">Maybe try:</span> <a href="${escapeHTML(analysis.suggested_url)}" target="_blank" rel="noopener noreferrer" style="color: inherit; text-decoration: underline;">${escapeHTML(analysis.suggested_url)}</a>`,
          "error"
        );
      } else {
        showMessage(el.dashboardMessage, analysis.message, "error");
      }
      showView("dashboard-view");
      return;
    }

    const entry = await saveAnalysis({
      ...analysis,
      url: state.targetTab.url,
      title: state.targetTab.title || "Untitled page",
      domain: getDomain(state.targetTab.url),
      analyzedAt: Date.now(),
      wasTruncated: prepared.wasTruncated,
    });

    await sendBg({
      type: "SET_BADGE",
      tabId: state.targetTabId,
      score: analysis.risk_score,
    });

    renderAnalysis(entry, el);
    document.getElementById("btn-export").onclick = () => exportAnalysis(entry);
    await renderDashboard(state, el, providers);
    await renderHistory(el, openHistoryEntry);
    showView("analysis-view");
  } catch (error) {
    hideLoading();
    showMessage(el.dashboardMessage, getUserMessage(error), "error");
    showView("dashboard-view");
  }
}

/* ─── History ────────────────────────────────────────────────── */

function openHistoryEntry(id, history) {
  const entry = history.find((item) => item.id === id);
  if (!entry) return;
  renderAnalysis(entry, el);
  document.getElementById("btn-export").onclick = () => exportAnalysis(entry);
  showView("analysis-view");
}

async function handleClearHistory() {
  const confirmed = await showConfirmModal("Clear all saved analyses?");
  if (!confirmed) return;
  await clearHistory();
  await renderHistory(el, openHistoryEntry);
  renderDashboard(state, el, providers);
}

/* ─── View switching ─────────────────────────────────────────── */

function showView(viewId) {
  for (const view of el.views) {
    view.classList.toggle("hidden", view.id !== viewId);
  }
  for (const btn of el.viewButtons) {
    btn.classList.toggle(
      "nav-button--active",
      btn.dataset.viewButton === viewId,
    );
  }
  window.scrollTo({ top: 0, behavior: "instant" });
}

/* ─── Loading ────────────────────────────────────────────────── */

function setLoading(message) {
  el.loadingMessage.textContent = message;
  el.loadingOverlay.classList.remove("hidden");
}

function hideLoading() {
  el.loadingOverlay.classList.add("hidden");
}

/* ─── Chrome messaging ───────────────────────────────────────── */

async function sendBg(message) {
  const response = await chrome.runtime.sendMessage(message);
  if (!response || response.success === false || response.error) {
    throw new Error(response?.error || "Extension message failed.");
  }
  return response;
}

/* ─── Tab helpers ────────────────────────────────────────────── */

async function getTargetTab(tabId) {
  if (!Number.isInteger(tabId)) return null;
  try {
    return await chrome.tabs.get(tabId);
  } catch {
    return null;
  }
}

/* ─── Model limit ────────────────────────────────────────────── */

function getModelLimit(settings) {
  const provider = providers[settings.provider];
  const model = provider?.models.find((m) => m.id === settings.model);

  if (provider?.analysisMaxInputTokens) return provider.analysisMaxInputTokens;
  if (model?.analysisMaxInputTokens) return model.analysisMaxInputTokens;
  if (!model) return 8000;

  // Cap at 8,000 tokens to prevent massive API credit drain.
  // 8k tokens ≈ 32,000 characters, enough for the vast majority of T&Cs.
  return Math.min(8000, Math.floor(model.maxTokens * 0.25));
}
