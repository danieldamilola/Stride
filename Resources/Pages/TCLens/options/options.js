/**
 * @file options.js
 * @description Dashboard orchestration for T&C Lens.
 *              Handles bootstrap, state, event wiring, view routing,
 *              settings form, and analysis flow coordination.
 */

import {
  analyze,
  estimateCost,
  fetchTCLensBalance,
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

/** @type {{ settings: Object|null, targetTab: Object|null, targetTabId: number|null, tclensBalance: Object|null }} */
const state = {
  settings: null,
  targetTab: null,
  targetTabId: null,
  tclensBalance: null,
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

  // T&C Lens AI credits card
  el.tclensCredits = document.getElementById("tclens-credits");
  el.tclensTier = document.getElementById("tclens-tier");
  el.tclensCount = document.getElementById("tclens-count");
  el.tclensFill = document.getElementById("tclens-fill");
  el.tclensTotal = document.getElementById("tclens-total");
  el.tclensUsed = document.getElementById("tclens-used");
  el.tclensWarning = document.getElementById("tclens-warning");
  el.tclensNote = document.getElementById("tclens-note");

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
  el.apiKeyInput.addEventListener("input", () => {
    updateCostEstimate();
    refreshTCLensBalance();
  });

  el.refreshModelsBtn.addEventListener("click", refreshAvailableModels);
  el.settingsForm.addEventListener("submit", handleSettingsSubmit);
  el.clearHistoryBtn.addEventListener("click", handleClearHistory);
}

/* ─── Initial load ───────────────────────────────────────────── */

async function loadInitialState() {
  state.settings = await getSettings();
  applySettingsToForm(state.settings);
  state.settings = { ...state.settings, model: el.modelSelect.value };

  try {
    const stored = await chrome.storage.local.get("targetTabId");
    state.targetTabId = stored.targetTabId;
    state.targetTab = await getTargetTab(state.targetTabId);
  } catch (e) {
    console.warn("Chrome storage not available, running in native Stride mode.");
  }

  await renderDashboard(state, el, providers);
  updateCostEstimate();
  await renderHistory(el, openHistoryEntry);

  if (window.chrome?.webview) {
    window.chrome.webview.postMessage("tclens-get-text:");
  }
}

/* ─── Provider / model selects ───────────────────────────────── */

function populateProviderOptions() {
  el.providerSelect.replaceChildren();
  for (const [id, p] of Object.entries(providers)) {
    const opt = document.createElement("option");
    opt.value = id;
    opt.textContent = p.name;
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
  el.apiKeyInput.value = settings.apiKey || "";
  handleProviderChange(settings.model);
}

function handleProviderChange(selectedModel) {
  const providerId = el.providerSelect.value;
  const provider = providers[providerId];

  // Toggle the hosted T&C Lens AI credits card
  const isTCLens = providerId === "tclens";
  el.tclensCredits.classList.toggle("hidden", !isTCLens);
  if (!isTCLens) state.tclensBalance = null;

  if (selectedModel) {
    populateModelOptions(providerId, selectedModel);
  } else {
    populateModelOptions(providerId, provider.models[0].id);
  }

  refreshTCLensBalance();
}

function updateCostEstimate() {
  const providerId = el.providerSelect.value;
  const hasKey = Boolean(el.apiKeyInput.value.trim());

  if (!hasKey) {
    el.costEstimate.textContent = "";
    return;
  }

  if (providerId === "tclens") {
    el.costEstimate.textContent = "";
    return;
  }

  const cost = estimateCost("", providerId, el.modelSelect.value);
  if (!cost) {
    el.costEstimate.textContent = "";
    return;
  }

  if (cost.totalCost === 0) {
    el.costEstimate.textContent = "own AI";
  } else {
    el.costEstimate.textContent = `~$${cost.totalCost.toFixed(4)} base cost`;
  }
}

/* ─── T&C Lens AI credits ────────────────────────────────────── */

let balanceTimer;

function refreshTCLensBalance() {
  const providerId = el.providerSelect.value;
  const apiKey = el.apiKeyInput.value.trim();

  el.tclensCredits.classList.toggle("hidden", providerId !== "tclens");
  clearTimeout(balanceTimer);
  if (providerId !== "tclens") return;

  if (!apiKey) {
    resetTCLensPanel("Paste your tcl_live key above to check your balance.");
    return;
  }

  setTCLensLoading();
  balanceTimer = setTimeout(async () => {
    if (el.providerSelect.value !== "tclens") return;
    try {
      state.tclensBalance = await fetchTCLensBalance(apiKey);
      renderTCLensCredits(state.tclensBalance);
    } catch {
      state.tclensBalance = null;
      resetTCLensPanel(
        "Could not verify this key. Check it and try again.",
      );
    }
  }, 400);
}

function setTCLensLoading() {
  el.tclensCount.textContent = "…";
  el.tclensTotal.textContent = "—";
  el.tclensUsed.textContent = "—";
  if (el.tclensTier) {
    el.tclensTier.textContent = "—";
    el.tclensTier.className = "credits-tier";
  }
  el.tclensWarning.classList.add("hidden");
  el.tclensNote.textContent = "Checking your balance…";
  updateCostEstimate();
}

function renderTCLensCredits(balance) {
  const credits = Number(balance.credits) || 0;
  const total = Number(balance.total_purchased_credits) || 0;
  const actualTotal = Math.max(total, credits);
  const pctUsed =
    actualTotal > 0
      ? Math.max(0, Math.min(100, Math.round(((actualTotal - credits) / actualTotal) * 100)))
      : 0;
  const paid = total > 1;

  el.tclensCount.textContent = credits;
  el.tclensFill.style.width = `${pctUsed}%`;
  el.tclensTotal.textContent = `${actualTotal} credits purchased`;
  el.tclensUsed.textContent = `${pctUsed}% used`;
  if (el.tclensTier) {
    el.tclensTier.textContent = paid ? "paid" : "free";
    el.tclensTier.className = `credits-tier${paid ? " credits-tier--paid" : ""}`;
  }

  const low = credits < 20;
  el.tclensWarning.textContent = low
    ? "Running low, buy more at tclens.me."
    : "";
  el.tclensWarning.classList.toggle("hidden", !low);

  el.tclensNote.textContent = paid
    ? "Paid plan, need more? Visit tclens.me."
    : "Free tier, 1 free credit included with signup.";
  updateCostEstimate();
}

function resetTCLensPanel(note) {
  el.tclensCount.textContent = "—";
  el.tclensFill.style.width = "0%";
  el.tclensTotal.textContent = "—";
  el.tclensUsed.textContent = "—";
  if (el.tclensTier) {
    el.tclensTier.textContent = "—";
    el.tclensTier.className = "credits-tier";
  }
  el.tclensWarning.classList.add("hidden");
  el.tclensNote.textContent = note;
  updateCostEstimate();
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
    if (state.settings.provider === "tclens") refreshTCLensBalance();
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

/* ─────────────────────────────────────────────────────────────
 * Stride Integration: tclensProcessInjection
 * ───────────────────────────────────────────────────────────── */
window.tclensProcessInjection = async (payload) => {
  window.__strideTCLensPayload = payload;
  state.targetTabId = -1;
  state.targetTab = { url: payload.url, title: payload.title };
  await renderDashboard(state, el, providers);
};

/* ─────────────────────────────────────────────────────────────
 * Chrome messaging 
 * ───────────────────────────────────────────────────────────── */

async function sendBg(message) {
  if (window.__strideTCLensPayload) {
    if (message.type === "INJECT_SCRAPER") return { success: true };
    if (message.type === "EXTRACT_PAGE_TEXT") return { text: window.__strideTCLensPayload.text, success: true };
    if (message.type === "SET_BADGE") return { success: true };
  }
  if (!window.chrome?.runtime?.sendMessage) {
      console.warn("Running outside extension, ignoring sendBg.");
      return { success: true };
  }
  const response = await chrome.runtime.sendMessage(message);
  if (!response || response.success === false || response.error) {
    throw new Error(response?.error || "Extension message failed.");
  }
  return response;
}

/* ─────────────────────────────────────────────────────────────
 * Tab helpers 
 * ───────────────────────────────────────────────────────────── */

async function getTargetTab(tabId) {
  if (tabId === -1 && window.__strideTCLensPayload) {
    return { url: window.__strideTCLensPayload.url, title: window.__strideTCLensPayload.title };
  }
  if (!Number.isInteger(tabId)) return null;
  try {
    return window.chrome?.tabs?.get ? await chrome.tabs.get(tabId) : null;
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

