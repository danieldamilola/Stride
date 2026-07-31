/**
 * @file renderer.js
 * @description DOM rendering functions for T&C Lens dashboard and analysis views.
 * @module options/renderer
 */

import {
  escapeHTML,
  setHTML,
  getRiskLevel,
  formatDate,
  copyToClipboard,
} from "./utils.js";
import { getLatestAnalysisForUrl, getHistory } from "../lib/storage.js";

/**
 * Render the dashboard view with target and provider status.
 *
 * @param {Object} state - Application state.
 * @param {Object} el - Element references.
 * @param {Object} providers - Provider configuration map.
 */
export async function renderDashboard(state, el, providers) {
  renderTargetCell(state, el);
  renderProviderCell(state, el, providers);
  await renderLastAnalysis(state, el);
}

/**
 * Render the target page status cell.
 *
 * @param {Object} state - Application state.
 * @param {Object} el - Element references.
 */
export function renderTargetCell(state, el) {
  if (!state.targetTab) {
    el.targetStatus.className = "status-dot status-dot--danger";
    el.targetTitle.textContent = "No page selected";
    el.targetUrl.textContent =
      "Go to a Terms or Privacy page and click the extension icon.";
    el.analyzeButton.disabled = true;
    return;
  }

  el.targetStatus.className = "status-dot status-dot--ok";
  el.targetTitle.textContent = state.targetTab.title || "Untitled page";
  el.targetUrl.textContent = state.targetTab.url || "";
  el.analyzeButton.disabled = false;
}

/**
 * Render the AI provider status cell.
 *
 * @param {Object} state - Application state.
 * @param {Object} el - Element references.
 * @param {Object} providers - Provider configuration map.
 */
export function renderProviderCell(state, el, providers) {
  const provider = providers[state.settings.provider];
  const hasKey = Boolean(state.settings.apiKey?.trim());
  const name = provider?.name || state.settings.provider;

  el.providerName.textContent = name;
  el.apiStatus.className = hasKey
    ? "status-dot status-dot--ok"
    : "status-dot status-dot--warn";
  el.providerStatus.textContent = hasKey
    ? `${el.modelSelect.value || state.settings.model}`
    : "No API key — go to Settings.";
}

/**
 * Render the "last analysis" metadata line on the dashboard.
 *
 * @param {Object} state - Application state.
 * @param {Object} el - Element references.
 */
export async function renderLastAnalysis(state, el) {
  if (!state.targetTab?.url) {
    el.lastAnalysis.textContent = "";
    return;
  }

  const latest = await getLatestAnalysisForUrl(state.targetTab.url);
  el.lastAnalysis.textContent = latest
    ? `Last run ${formatDate(latest.analyzedAt)} · score ${latest.risk_score}`
    : "";
}

/**
 * Render a completed AI analysis to the analysis view.
 * Generates the score, summary, findings, and wires copy buttons.
 *
 * @param {Object} analysis - Parsed analysis object.
 * @param {Object} el - Element references.
 */
export function renderAnalysis(analysis, el) {
  const riskLevel = getRiskLevel(analysis.risk_score);
  const findings = analysis.findings.map(renderFinding).join("");

  const truncationNotice = analysis.wasTruncated
    ? `<div class="truncation-notice">Document was too long — only the first portion was analyzed.</div>`
    : "";

  setHTML(
    el.analysisResults,
    `
    <div class="analysis-header">
      <div class="score-block">
        <div class="score-number">${analysis.risk_score}</div>
        <div class="score-label">${riskLevel} risk</div>
        <div class="score-bar">
          <div class="score-bar-fill" data-target-width="${analysis.risk_score}"></div>
        </div>
      </div>
      <div class="analysis-meta">
        <div class="analysis-title">${escapeHTML(analysis.title || analysis.domain || "Analysis")}</div>
        <p class="analysis-summary">${escapeHTML(analysis.summary)}</p>
        ${truncationNotice}
      </div>
    </div>
    <div class="findings-list">${findings}</div>
  `,
  );

  // Animate the score bar from 0% to its target width on the next frame
  requestAnimationFrame(() => {
    const fill = el.analysisResults.querySelector(".score-bar-fill");
    if (fill) {
      fill.style.width = `${fill.dataset.targetWidth}%`;
    }
  });

  // Wire copy buttons on quotes
  for (const btn of el.analysisResults.querySelectorAll("[data-copy]")) {
    btn.addEventListener("click", async () => {
      const text = btn.dataset.copy;
      await copyToClipboard(text);
      const original = btn.textContent;
      btn.textContent = "Copied";
      setTimeout(() => {
        btn.textContent = original;
      }, 1500);
    });
  }
}

/**
 * Render a single finding as an HTML string.
 *
 * @param {Object} finding - Finding object with title, importance, description, quote.
 * @returns {string} HTML string for the finding.
 */
function renderFinding(finding) {
  const quoteHtml = finding.quote
    ? `<blockquote class="finding-quote">
        <span class="finding-quote-text">${escapeHTML(finding.quote)}</span>
        <button class="btn-copy" data-copy="${escapeHTML(finding.quote)}" title="Copy quote">Copy</button>
       </blockquote>`
    : "";

  return `
    <article class="finding">
      <div class="finding-header">
        <h2 class="finding-title">${escapeHTML(finding.title)}</h2>
        <span class="importance-tag importance-tag--${escapeHTML(finding.importance)}">${escapeHTML(finding.importance)}</span>
      </div>
      <p class="finding-description">${escapeHTML(finding.description)}</p>
      ${quoteHtml}
    </article>
  `;
}

/**
 * Render the analysis history list in the settings view.
 *
 * @param {Object} el - Element references.
 * @param {Function} onOpenEntry - Callback when a history item is clicked.
 */
export async function renderHistory(el, onOpenEntry) {
  const history = await getHistory();

  if (history.length === 0) {
    setHTML(el.historyList, `<p class="history-empty">No analyses yet.</p>`);
    return;
  }

  const historyHtml = history
    .map(
      (entry) => `
    <div class="history-item" data-history-id="${escapeHTML(entry.id)}">
      <div class="history-item-info">
        <div class="history-item-title">${escapeHTML(entry.title || entry.domain || entry.url)}</div>
        <div class="history-item-meta">${escapeHTML(entry.domain || "")} · ${formatDate(entry.analyzedAt)}</div>
      </div>
      <span class="history-item-score">${entry.risk_score}</span>
    </div>
  `,
    )
    .join("");

  setHTML(el.historyList, historyHtml);

  for (const item of el.historyList.querySelectorAll("[data-history-id]")) {
    item.addEventListener("click", () =>
      onOpenEntry(item.dataset.historyId, history),
    );
  }
}
