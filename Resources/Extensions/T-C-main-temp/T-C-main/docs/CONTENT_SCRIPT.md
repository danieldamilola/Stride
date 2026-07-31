# T&C Lens — Content Script (Page Scraper)

## Role

The content script (`content/scraper.js`) is responsible for **extracting readable text content** from any web page the user navigates to. It runs in the target tab's context, giving it DOM access, but it shares no JavaScript scope with the page's own scripts (Chrome's isolated world).

## Design Philosophy

### On-Demand Injection

Unlike most extensions that auto-inject content scripts on every page load (declared in `manifest.json`), T&C Lens **injects the scraper only when needed** — specifically, when the user clicks the toolbar icon and the options page requests analysis.

**Why this matters:**

- **Performance:** No script runs on pages the user isn't analyzing
- **Privacy:** No code is injected silently — injection only happens after an explicit user action
- **Compatibility:** Reduces chance of conflicts with page scripts
- **Permissions:** Combined with `activeTab`, Chrome grants temporary access only to the relevant tab

### Read-Only

The content script **never modifies the page DOM**. It only reads text content and sends it back. This means:

- It cannot break page functionality
- It cannot be detected by the page's scripts (no new elements, no modified attributes)
- It leaves no trace on the page

## Extraction Strategy

Web pages have wildly different structures — some use semantic HTML, some are a mess of nested divs. The scraper uses a **smart fallback chain** to maximize text extraction quality:

```
Priority 1: <article> element
    ↓ (if not found)
Priority 2: <main> element
    ↓ (if not found)
Priority 3: Largest <div> by text content volume
    ↓ (if empty or not found)
Priority 4: All <p> elements concatenated
    ↓ (if empty)
Priority 5: document.body.innerText (last resort)
```

## Code Implementation

```javascript
// content/scraper.js — T&C Lens Page Text Extractor

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === "EXTRACT_PAGE_TEXT") {
    try {
      const text = extractPageText();
      sendResponse({ text: text, success: true });
    } catch (error) {
      sendResponse({ text: "", success: false, error: error.message });
    }
  }
  return true; // Keep message channel open
});

function extractPageText() {
  // Strategy 1: Look for <article> element
  const article = document.querySelector("article");
  if (article && article.textContent.trim().length > 100) {
    return cleanText(article.textContent);
  }

  // Strategy 2: Look for <main> element
  const main = document.querySelector("main");
  if (main && main.textContent.trim().length > 100) {
    return cleanText(main.textContent);
  }

  // Strategy 3: Find the largest div by text content
  const allDivs = document.querySelectorAll("div");
  let largestDiv = null;
  let largestLength = 0;

  allDivs.forEach((div) => {
    const length = div.textContent.trim().length;
    if (length > largestLength) {
      largestLength = length;
      largestDiv = div;
    }
  });

  if (largestDiv && largestLength > 200) {
    return cleanText(largestDiv.textContent);
  }

  // Strategy 4: Concatenate all <p> elements
  const paragraphs = document.querySelectorAll("p");
  const combinedText = Array.from(paragraphs)
    .map((p) => p.textContent.trim())
    .filter((text) => text.length > 0)
    .join("\n\n");

  if (combinedText.length > 100) {
    return cleanText(combinedText);
  }

  // Strategy 5: Last resort — entire body text
  return cleanText(document.body.innerText);
}

function cleanText(rawText) {
  return rawText
    .replace(/\n{3,}/g, "\n\n") // Collapse 3+ newlines into 2
    .replace(/\t/g, " ") // Replace tabs with spaces
    .replace(/  +/g, " ") // Collapse multiple spaces
    .trim();
}
```

## Text Cleaning Rules

Raw DOM text extraction produces messy output. The `cleanText()` function normalizes it:

| Issue                       | Before Cleaning           | After Cleaning        |
| --------------------------- | ------------------------- | --------------------- |
| Excessive newlines          | `"text\n\n\n\nmore text"` | `"text\n\nmore text"` |
| Tab characters              | `"column1\tcolumn2"`      | `"column1 column2"`   |
| Multiple spaces             | `"word   another"`        | `"word another"`      |
| Leading/trailing whitespace | `"  text  "`              | `"text"`              |

## Edge Cases and Challenges

### Very Long Pages

Some T&C pages are extremely long (50,000+ characters). The extracted text may exceed the AI model's context window. The options page handles this by truncating the text before sending to the AI — the content script always extracts the full text, and trimming happens upstream.

**Why the scraper extracts everything:** We don't know the AI model's context limit at the scraper level. The options page knows the selected model and its limits, so it handles truncation. This keeps the scraper model-agnostic.

### Pages with No Extractable Text

Some pages render content dynamically (React, Vue, Angular) and may not have text in the DOM at the time of extraction. If the scraper returns empty text:

1. The options page shows: "Could not extract text from this page. The page may load content dynamically. Try scrolling the page first, then analyze again."
2. Users can also manually copy-paste T&C text into a text input field (future enhancement — not in V1).

### JavaScript-Rendered Content

The `chrome.scripting.executeScript` API executes the script after the page's `document_idle` state (by default), which means the DOM should be fully loaded. However, some Single Page Applications (SPAs) render content after initial load. In these cases:

- The text extracted may be incomplete (just headers, navigation, etc.)
- The AI gatekeeper prompt will likely flag this as "not a T&C page"
- The user sees a clear message: "This doesn't look like a T&C page"
- For V1, this is acceptable. A V2 enhancement could add a delay or wait for specific DOM signals before extracting.

### Pages with Strict CSP

Some pages have Content Security Policies that block script injection. If `chrome.scripting.executeScript` fails, the background catches the error and the options page displays: "This page blocks external scripts. The extension cannot extract text from this page."

### Navigation Headers and Footers

The scraper may pick up navigation menus, footers, cookie banners, and other non-content text. This is handled in two ways:

1. **Extraction strategy:** By preferring `<article>` and `<main>` elements, we naturally exclude nav bars and footers that sit outside these semantic containers.
2. **AI gatekeeper:** The AI prompt instructs the model to focus on legal/agreement content and ignore non-relevant text. Even if some noise gets through, the AI should filter it out in its analysis.

### iframe Content

Some T&C pages embed content in iframes (e.g., third-party cookie consent notices). The content script only has access to the top-level document. If the actual T&C content is inside an iframe:

- The scraper will miss it
- The extracted text will be incomplete
- The AI gatekeeper will flag it as not a T&C page
- For V1, this is a known limitation. Could be addressed in V2 with recursive iframe extraction.

## Message Protocol

### Incoming Message

```javascript
{
  type: "EXTRACT_PAGE_TEXT";
}
```

No additional parameters needed — the content script knows what to do.

### Outgoing Response (Success)

```javascript
{
  text: "Terms and Conditions...\n\n1. Acceptance of Terms...",
  success: true
}
```

### Outgoing Response (Failure)

```javascript
{
  text: '',
  success: false,
  error: "Description of what went wrong"
}
```

## Testing the Content Script

### Manual Testing Steps

1. Load the unpacked extension
2. Navigate to a known T&C page (e.g., `https://example.com/terms`)
3. Click the T&C Lens icon
4. In the options page, click "Analyze"
5. Open DevTools on the original tab (the T&C page)
6. Check the Console for extraction logs
7. Compare the extracted text with what's visible on the page

### Test Pages to Verify Against

| Page                                     | Expected Behavior                                     |
| ---------------------------------------- | ----------------------------------------------------- |
| `https://policies.google.com/privacy`    | Long privacy policy — should extract substantial text |
| `https://example.com`                    | Simple page — should extract minimal text             |
| A SPA with dynamic rendering             | May extract incomplete text                           |
| A page with `<article>` wrapping the T&C | Should use Strategy 1                                 |
| A page with `<main>` wrapping the T&C    | Should use Strategy 2                                 |
| A page with no semantic HTML             | Falls back to largest div or paragraphs               |
| An empty page                            | Returns empty string — AI gatekeeper catches it       |

### Console Logging (Development Only)

During development, the content script should log which extraction strategy it used:

```javascript
function extractPageText() {
  const article = document.querySelector("article");
  if (article && article.textContent.trim().length > 100) {
    console.log("[T&C Lens] Using Strategy 1: <article> element");
    return cleanText(article.textContent);
  }
  // ... etc for each strategy
}
```

This logging should be removed or disabled in production builds.
