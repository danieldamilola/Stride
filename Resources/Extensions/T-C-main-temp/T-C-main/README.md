<div align="center">

<img src="icons/icon128.png" width="80" alt="T&C Lens logo" />

# T&C Lens

**AI-powered Terms & Conditions analyzer.** Understand what you're agreeing to.

[![License: MIT](https://img.shields.io/badge/License-MIT-white.svg)](LICENSE)
[![Chrome MV3](https://img.shields.io/badge/Chrome-MV3-white.svg?logo=googlechrome&logoColor=white)](https://developer.chrome.com/docs/extensions/mv3/)
[![Firefox](https://img.shields.io/badge/Firefox-Supported-white.svg?logo=firefox&logoColor=white)](#firefox)
[![Zero Dependencies](https://img.shields.io/badge/Dependencies-0-white.svg)](#tech-stack)

</div>

---

T&C Lens is a browser extension that reads the fine print so you don't have to. Point it at any Terms of Service, Privacy Policy, or legal agreement — it extracts the text, sends it to your AI provider of choice, and returns a plain-English summary with a risk score and finding-by-finding breakdown.

Bring your own API key. No account required. No data leaves your browser except the text sent to your chosen AI.

<!-- Screenshots go here — replace with actual screenshots when available -->
<!-- <div align="center">
  <img src="docs/screenshots/dashboard.png" width="720" alt="T&C Lens dashboard" />
</div> -->

## Highlights

**Bring Your Own Key** — Works with 9 providers: Gemini, OpenAI, Anthropic, Groq, DeepSeek, Mistral, xAI, OpenRouter, and Together AI. Use free tiers or your existing API keys.

**Smart Extraction** — Intelligent scraper targets legal content specifically, filtering out navigation, headers, footers, and cookie banners.

**Privacy-First** — No telemetry, no analytics, no data collection. Your API key stays in local storage. The only external request is the one you trigger to your chosen AI.

**On-Demand Only** — Fully passive. Does nothing in the background. Runs only when you click.

**Risk Scoring** — Every analysis gets a 0–100 risk score with high/medium/low importance findings, direct quotes, and actionable explanations.

**History & Export** — Past analyses saved locally. Export any result as a `.txt` file.

**Minimal, Dark UI** — Designed to feel like a premium developer tool. No clutter. No colors. Just information.

## Quick Start

### Chrome

```bash
git clone https://github.com/danieldamilola/T-C.git
```

1. Open `chrome://extensions/`
2. Enable **Developer mode** (top right)
3. Click **Load unpacked** → select the cloned `T-C` folder
4. The T&C Lens icon appears in your toolbar

### Firefox

1. Go to `about:debugging#/runtime/this-firefox`
2. Click **Load Temporary Add-on** → select `manifest.json` from the cloned folder

> [!NOTE]
> Firefox uses background scripts instead of a service worker. Some behavior may differ slightly.

## Setup

1. Click the **T&C Lens** icon in your toolbar
2. Go to **Settings**
3. Select a provider and paste your API key
4. Click **Refresh available models** to fetch the latest models
5. **Save**

> [!TIP]
> Don't have an API key? **T&C Lens AI** (a hosted, no-key-needed option) is coming soon. Star this repo to stay updated.

## Usage

1. Navigate to any Terms of Service, Privacy Policy, or legal agreement
2. Click the T&C Lens icon — or right-click → **Analyze with T&C Lens**
3. Review the risk score, summary, and finding-by-finding breakdown
4. Export or revisit past analyses from **History**

## Supported Providers

| Provider | Free Tier | Models |
|----------|-----------|--------|
| **Google Gemini** | ✅ | Gemini 2.5 Flash, 2.5 Pro, 2.0 Flash |
| **Groq** | ✅ | Llama 3.3 70B, Llama 3.1 8B, DeepSeek R1 |
| **DeepSeek** | ✅ | DeepSeek Chat, DeepSeek Reasoner |
| **OpenRouter** | ✅ | Gemini, DeepSeek, Llama 3.3 70B |
| **Together AI** | ✅ | Llama 3.3 70B, DeepSeek V3, Qwen 2.5 72B |
| **Mistral** | — | Mistral Large, Mistral Small |
| **OpenAI** | — | GPT-4o, GPT-4o Mini |
| **Anthropic** | — | Claude Sonnet 4, Claude 3 Haiku |
| **xAI** | — | Grok 3, Grok 3 Mini |

## Architecture

```
T&C Lens
├── background.js          ← Service worker: tab routing, badge, context menu
├── content/scraper.js     ← Injected: extracts legal text from the page DOM
├── options/               ← Dashboard UI (HTML/CSS/JS modules)
│   ├── options.js         ← Orchestration and state management
│   ├── renderer.js        ← Analysis and dashboard rendering
│   ├── utils.js           ← Shared utilities
│   └── export.js          ← Text file export
├── lib/                   ← Core logic
│   ├── ai-client.js       ← Provider-agnostic API wrapper (with retry)
│   ├── providers.js       ← Provider registry and model normalization
│   ├── prompt.js          ← System prompt and message builder
│   ├── parser.js          ← AI response JSON extraction and validation
│   └── storage.js         ← Local storage CRUD
└── fonts/                 ← Self-hosted Inter and JetBrains Mono (WOFF2)
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full data flow, message-passing design, and security model.

## Tech Stack

- **Runtime**: Vanilla HTML, CSS, JavaScript — zero dependencies
- **Extension**: Chrome Manifest V3 with Firefox compatibility
- **Fonts**: Self-hosted Inter + JetBrains Mono (no CDN)
- **Tooling**: ESLint for linting
- **Design**: Custom design system documented in [DESIGN.md](DESIGN.md)

## Roadmap

- [ ] **T&C Lens AI** — Hosted provider option (no API key needed)
- [ ] Chrome Web Store listing
- [ ] Firefox Add-ons listing
- [ ] Screenshot & demo GIF for README
- [ ] Side-by-side comparison view
- [ ] Shareable analysis links

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a PR — it covers code standards, the 300-line file limit, naming conventions, and the architectural rules.

```bash
# Clone and install dev dependencies
git clone https://github.com/danieldamilola/T-C.git
cd T-C
npm install

# Lint
npm run lint
```

## License

[MIT](LICENSE)

---

<div align="center">

**[Report a bug](https://github.com/danieldamilola/T-C/issues)** · **[Request a feature](https://github.com/danieldamilola/T-C/issues)** · **[Star this project](https://github.com/danieldamilola/T-C)**

</div>
