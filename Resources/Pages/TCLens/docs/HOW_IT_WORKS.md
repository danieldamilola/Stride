# How T&C Lens Works

A plain-English guide to how T&C Lens reads and breaks down legal documents for you.

---

## The Short Version

You visit a website's Terms of Service or Privacy Policy. You click the T&C Lens icon. A few seconds later, you get a simple summary telling you what the document actually says, what's risky, and what you should care about.

That's it. No sign-up required (if you bring your own AI key), no data collected, nothing running in the background.

---

## Step by Step

### 1. You click the icon

When you're on a page with legal text (like a Terms of Service or Privacy Policy), you click the T&C Lens icon in your browser toolbar. You can also right-click anywhere on the page and select **"Analyze with T&C Lens"**.

### 2. The extension reads the page

T&C Lens looks at the page you're on and tries to find the legal content. It's smart about this — it skips things like:

- Navigation bars and menus
- Cookie pop-ups
- Sidebars and footers
- Ads and banners

It only grabs the actual legal text. Think of it like highlighting the important part of a textbook and ignoring the margins.

### 3. The text gets sent to an AI

The legal text gets sent to an AI model (like Google Gemini, DeepSeek, or OpenAI — whichever one you chose in Settings). The AI has been given specific instructions to act like a legal document expert.

The AI reads the entire document and produces:

- **A risk score** (0–100) — how concerning is this document overall?
- **A summary** — 2–3 sentences explaining what the document says in normal language
- **Findings** — specific things you should know about, each rated as high, medium, or low importance

### 4. You see the results

The extension shows you everything in a clean dashboard:

- A big number showing the overall risk score
- A color-coded label (like "HIGH RISK" or "LOW RISK")
- The summary in plain English
- Each finding with an explanation, why it matters, and the exact quote from the document that backs it up

### 5. You can save or export

Every analysis is automatically saved to your history. You can go back and look at old ones anytime. You can also export any analysis as a text file if you want to keep a copy.

---

## What's a "finding"?

A finding is one specific thing the AI noticed in the document. For example:

> **Broad Data Harvesting** — MEDIUM
>
> Google collects a wide range of user data, including search history, location information, and activity on third-party sites and apps.
>
> *"We use the information that we collect from all our services for the following purposes: Provide our services, Maintain & improve our services..."*

Each finding has:

- **A title** — what the issue is about
- **An importance level** — high, medium, or low
- **An explanation** — what it means in simple terms
- **A direct quote** — the exact words from the document that prove it

---

## Where does the AI come from?

T&C Lens does **not** have its own built-in AI. You have two options:

### Option A: Bring Your Own Key (free to use, you pay the AI provider directly)

You get an API key from an AI provider (like Google, OpenAI, or DeepSeek) and paste it into Settings. The extension sends the legal text directly to that provider and gets the analysis back.

Many providers offer **free tiers** — for example, Google Gemini and Groq let you make hundreds of requests per day at no cost.

### Option B: T&C Lens AI (coming soon)

If you don't want to deal with API keys, T&C Lens AI is a simple paid option. You create an account, buy credits, and just click analyze. We handle the AI part for you.

---

## What about privacy?

This is important, so here's exactly what happens with your data:

**What stays on your device:**
- Your API key
- Your analysis history
- Your settings

**What gets sent out:**
- The text of the legal document — sent **only** to whichever AI provider you chose
- Nothing else. No analytics, no tracking, no telemetry

**What we never do:**
- We never see your API key
- We never store your browsing history
- We never send data to our own servers (unless you use T&C Lens AI)
- We never run in the background — the extension does nothing until you click it

---

## How the risk score works

The AI reads the full document and assigns a score from 0 to 100:

| Score | Label | What it means |
|-------|-------|---------------|
| 0–30 | Low Risk | Standard terms, nothing unusual |
| 31–60 | Medium Risk | Some concerning clauses, worth reading |
| 61–100 | High Risk | Significant issues — data collection, liability waivers, etc. |

The score is based on things like:
- How much personal data is collected
- Whether your data is shared with third parties
- Whether you give up legal rights (like the right to sue)
- How much control the company keeps over your account and content
- Whether terms can change without telling you

---

## How the page reader works (the scraper)

When you click analyze, the extension needs to extract the legal text from the page. It uses four strategies, in order:

1. **Look for the main content area** — Most websites wrap their content in a `<main>` or `<article>` tag. If the extension finds one with enough text, it uses that.

2. **Look for legal-specific sections** — Some pages have sections specifically marked as terms or policies (through their CSS classes or IDs). The extension checks for those.

3. **Count paragraphs** — If the first two methods don't work, the extension looks for the area of the page with the most paragraph text. Legal documents tend to be the densest part of any page.

4. **Use everything** — As a last resort, it takes all the text on the page (minus navigation, headers, footers, and junk).

In all cases, it removes noisy elements like cookie banners, navigation menus, and sidebars before extracting the text.

---

## Supported AI Providers

| Provider | Free Tier? | Notes |
|----------|-----------|-------|
| Google Gemini | Yes | Most generous free tier, 1M token context |
| Groq | Yes | Very fast responses |
| DeepSeek | Yes | Cheapest paid rates |
| OpenRouter | Yes | Access to many models through one key |
| Together AI | Yes | Good open-source model selection |
| Mistral | No | Strong European AI provider |
| OpenAI | No | GPT-4o and GPT-4o Mini |
| Anthropic | No | Claude models |
| xAI | No | Grok models |

---

## Frequently Asked Questions

**Do I need to pay to use T&C Lens?**
No. If you bring your own API key from a free-tier provider (like Google Gemini or Groq), it costs you nothing.

**Is my data safe?**
Yes. Your data never touches our servers. It goes directly from your browser to the AI provider you selected. We can't see it even if we wanted to.

**Can I use this on any website?**
You can use it on any website that contains legal text. If you try to analyze a page that isn't a legal document (like a blog post or a homepage), the AI will tell you and suggest the correct URL for that site's terms.

**Does it work on Firefox?**
Yes. You can load it as a temporary add-on in Firefox. See the README for instructions.

**Does it run in the background?**
No. T&C Lens does absolutely nothing until you click the icon. It doesn't monitor your browsing, track your tabs, or use any resources when idle.

**What if the document is really long?**
The extension will send as much text as the AI model can handle. Most models support at least 128,000 tokens (roughly 100,000 words). If the document is longer than that, the extension analyzes the first portion and tells you it was truncated.

**Can I see what was sent to the AI?**
The extension sends two things: a system instruction (telling the AI how to analyze legal documents) and the text it extracted from the page. Nothing else.
