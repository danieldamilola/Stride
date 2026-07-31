/**
 * @file utils.js
 * @description Shared utility functions for T&C Lens options page.
 * @module options/utils
 */

/**
 * Escape HTML special characters to prevent injection.
 * Encodes &, <, >, ", ', and ` to their HTML entity equivalents.
 *
 * @param {*} value - Value to escape (coerced to string).
 * @returns {string} HTML-safe string.
 */
export function escapeHTML(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;")
    .replace(/`/g, "&#96;");
}

/**
 * Parse HTML string and set as element content.
 * Uses DOMParser to avoid innerHTML — callers must still escape
 * user-controlled content with escapeHTML() before building the
 * HTML string.
 *
 * @param {HTMLElement} element - Target DOM element.
 * @param {string} htmlString - HTML content to set.
 */
export function setHTML(element, htmlString) {
  const parser = new DOMParser();
  const doc = parser.parseFromString(htmlString, "text/html");
  element.replaceChildren(...doc.body.childNodes);
}

/**
 * Show a text feedback message in a message element.
 *
 * @param {HTMLElement} element - Message container element.
 * @param {string} message - Message text.
 * @param {string} type - Message type ("error" or "success").
 */
export function showMessage(element, message, type) {
  element.textContent = message;
  element.className = type === "error" ? "message message--error" : "message";
}

/**
 * Show an HTML feedback message in a message element.
 *
 * @param {HTMLElement} element - Message container element.
 * @param {string} htmlContent - Pre-escaped HTML content.
 * @param {string} type - Message type ("error" or "success").
 */
export function showHtmlMessage(element, htmlContent, type) {
  setHTML(element, htmlContent);
  element.className = type === "error" ? "message message--error" : "message";
}

/**
 * Hide a message element.
 *
 * @param {HTMLElement} element - Message container element.
 */
export function hideMessage(element) {
  element.textContent = "";
  element.className = "message hidden";
}

/**
 * Get the risk level label for a numeric score.
 *
 * @param {number} score - Risk score (0–100).
 * @returns {string} "high", "medium", or "low".
 */
export function getRiskLevel(score) {
  if (score >= 70) return "high";
  if (score >= 40) return "medium";
  return "low";
}

/**
 * Map a TCLError or generic Error to a user-friendly message.
 *
 * @param {Error} error - Error object.
 * @returns {string} User-facing error message.
 */
export function getUserMessage(error) {
  const messages = {
    NO_API_KEY: "Add your API key in Settings first.",
    INVALID_PROVIDER: "Invalid AI provider selected.",
    INVALID_KEY: "API key rejected — check Settings.",
    RATE_LIMITED: "Rate limit reached. Wait a moment and try again.",
    NETWORK_ERROR: "Could not reach the AI service. Check your connection.",
    TIMEOUT: "The AI took too long to respond. Try again.",
    RESPONSE_ERROR: error.message,
  };

  if (error.message?.startsWith("PARSE_ERROR")) {
    return "Unexpected response from the AI. Try again.";
  }

  return messages[error.code] || error.message || "Something went wrong.";
}

/**
 * Extract the hostname from a URL string.
 *
 * @param {string} url - Full URL.
 * @returns {string} Hostname or empty string on failure.
 */
export function getDomain(url) {
  try {
    return new URL(url).hostname;
  } catch {
    return "";
  }
}

/**
 * Format a timestamp into a short human-readable date string.
 *
 * @param {number} timestamp - Unix timestamp in milliseconds.
 * @returns {string} Formatted date (e.g. "Jun 14, 05:30 PM").
 */
export function formatDate(timestamp) {
  return new Intl.DateTimeFormat(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(timestamp));
}

/**
 * Copy text to the clipboard.
 *
 * @param {string} text - Text to copy.
 */
export async function copyToClipboard(text) {
  try {
    await navigator.clipboard.writeText(text);
  } catch (err) {
    console.error("Failed to copy text: ", err);
  }
}

/**
 * Show a custom confirmation modal and return the user's choice.
 * Replaces the native confirm() dialog to maintain the dark UI design.
 *
 * @param {string} message - Confirmation message to display.
 * @returns {Promise<boolean>} True when the user confirms, false when cancelled.
 */
export function showConfirmModal(message) {
  return new Promise((resolve) => {
    const overlay = document.getElementById("confirm-overlay");
    const messageEl = document.getElementById("confirm-message");
    const confirmBtn = document.getElementById("confirm-yes");
    const cancelBtn = document.getElementById("confirm-no");

    messageEl.textContent = message;
    overlay.classList.remove("hidden");

    function cleanup(result) {
      overlay.classList.add("hidden");
      confirmBtn.removeEventListener("click", onConfirm);
      cancelBtn.removeEventListener("click", onCancel);
      resolve(result);
    }

    function onConfirm() {
      cleanup(true);
    }
    function onCancel() {
      cleanup(false);
    }

    confirmBtn.addEventListener("click", onConfirm);
    cancelBtn.addEventListener("click", onCancel);
  });
}
