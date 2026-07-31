/**
 * @file scraper.js
 * @description On-demand, read-only page text extractor for T&C Lens.
 *              Prioritises legal-document structure and excludes navigation,
 *              headers, footers, cookie banners, and other page chrome.
 */

if (!globalThis.tcLensScraperRegistered) {
  globalThis.tcLensScraperRegistered = true;

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.type !== "EXTRACT_PAGE_TEXT") return false;

    try {
      const { text, truncated, charCount } = extractPageText();
      sendResponse({ success: true, text, truncated, charCount });
    } catch (error) {
      sendResponse({ success: false, text: "", error: error.message });
    }

    return true;
  });
}

/* ─── Selectors to exclude ────────────────────────────────────── */

const NOISE_SELECTORS = [
  "header",
  "footer",
  "nav",
  "aside",
  "[role='banner']",
  "[role='navigation']",
  "[role='complementary']",
  ".cookie-banner",
  ".cookie-notice",
  "#cookie-banner",
  "#cookie-notice",
  ".site-header",
  ".site-footer",
  ".page-header",
  ".nav",
  ".navbar",
  ".sidebar",
  ".breadcrumb",
  "script",
  "style",
  "noscript",
  "iframe",
].join(", ");

/* ─── Main extraction ────────────────────────────────────────── */

function extractPageText() {
  // Semantic containers first (main, article) to avoid false positives
  // from sidebars or banners matching legal-keyword selectors.
  const text =
    getBySemanticContainer() ||
    getByLegalSelectors() ||
    getByParagraphDensity() ||
    getFullBodyText();

  return {
    text,
    truncated: false,
    charCount: text.length,
  };
}

/* ─── Strategy 1: Legal-specific selectors ───────────────────── */

function getByLegalSelectors() {
  const candidates = [
    // Common class/id patterns on legal pages
    "#terms",
    "#privacy",
    "#tos",
    "#legal",
    "#terms-of-service",
    "#privacy-policy",
    "#terms-of-use",
    "[class*='terms']",
    "[class*='privacy']",
    "[class*='legal']",
    "[class*='policy']",
    "[class*='agreement']",
    // Common structural patterns
    "article",
    "main",
    "[role='main']",
    "[role='article']",
  ];

  for (const selector of candidates) {
    const el = document.querySelector(selector);
    if (!el) continue;

    const cleaned = extractFromElement(el);
    if (cleaned.length > 500) return cleaned;
  }

  return "";
}

/* ─── Strategy 2: Semantic containers ───────────────────────── */

function getBySemanticContainer() {
  // Try <main> or first <section> with substantial text
  for (const selector of ["main", "section", "[role='main']"]) {
    const el = document.querySelector(selector);
    if (!el) continue;
    const text = extractFromElement(el);
    if (text.length > 300) return text;
  }
  return "";
}

/* ─── Strategy 3: Paragraph density ─────────────────────────── */

function getByParagraphDensity() {
  // Find the container that holds the most <p> text
  const blocks = Array.from(document.querySelectorAll("div, section, article"));
  let best = { el: null, density: 0 };

  for (const block of blocks) {
    // Skip if it's a noise element
    if (block.matches(NOISE_SELECTORS)) continue;

    const paragraphs = block.querySelectorAll("p");
    const totalText = Array.from(paragraphs).reduce(
      (acc, p) => acc + (p.textContent?.length || 0),
      0,
    );

    if (totalText > best.density) {
      best = { el: block, density: totalText };
    }
  }

  if (best.el && best.density > 500) {
    return extractFromElement(best.el);
  }

  return "";
}

/* ─── Fallback: full body ────────────────────────────────────── */

function getFullBodyText() {
  return cleanText(document.body?.innerText || "");
}

/* ─── Element text extractor (strips noise children) ─────────── */

function extractFromElement(el) {
  // Clone and remove noise nodes
  const clone = el.cloneNode(true);

  for (const noise of clone.querySelectorAll(NOISE_SELECTORS)) {
    noise.remove();
  }

  return cleanText(clone.innerText || clone.textContent || "");
}

/* ─── Text cleaning ──────────────────────────────────────────── */

function cleanText(rawText) {
  return rawText
    .replace(/\r\n/g, "\n")
    .replace(/\r/g, "\n")
    .replace(/\t/g, " ")
    .replace(/  +/g, " ")
    .replace(/\n{3,}/g, "\n\n")
    .replace(/[ \t]+\n/g, "\n")
    .trim();
}
