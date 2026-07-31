# T&C Lens — AI Client Design

## Role

The AI client (`lib/ai-client.js`) is a **provider-agnostic API wrapper** that handles communication with OpenAI, Anthropic, and Google Gemini. It abstracts away the differences between these APIs so the rest of the extension doesn't need to know which provider is being used.

## Design Principles

- **Single interface, multiple providers** — The main `analyze()` function accepts provider, API key, model, and text. It routes to the correct API implementation internally.
- **No external dependencies** — Uses the browser's native `fetch()` API. No Axios, no SDK.
- **Error normalization** — All provider-specific errors are caught and converted to a standard error format that the options page can display to the user.
- **Token-aware** — Knows each model's context limits and helps the caller avoid exceeding them.

## Provider Configurations

```javascript
const PROVIDERS = {
  openai: {
    name: "OpenAI",
    models: [
      {
        id: "gpt-4o",
        name: "GPT-4o",
        maxTokens: 128000,
        inputCost: 2.5,
        outputCost: 10.0,
      },
      {
        id: "gpt-4o-mini",
        name: "GPT-4o-mini",
        maxTokens: 128000,
        inputCost: 0.15,
        outputCost: 0.6,
      },
    ],
    baseUrl: "https://api.openai.com/v1/chat/completions",
  },
  anthropic: {
    name: "Anthropic",
    models: [
      {
        id: "claude-sonnet-4-20250514",
        name: "Claude 3.5 Sonnet",
        maxTokens: 200000,
        inputCost: 3.0,
        outputCost: 15.0,
      },
      {
        id: "claude-3-haiku-20240307",
        name: "Claude 3 Haiku",
        maxTokens: 200000,
        inputCost: 0.25,
        outputCost: 1.25,
      },
    ],
    baseUrl: "https://api.anthropic.com/v1/messages",
  },
  gemini: {
    name: "Google Gemini",
    models: [
      {
        id: "gemini-1.5-flash",
        name: "Gemini 1.5 Flash",
        maxTokens: 1000000,
        inputCost: 0,
        outputCost: 0,
      },
      {
        id: "gemini-1.5-pro",
        name: "Gemini 1.5 Pro",
        maxTokens: 2000000,
        inputCost: 1.25,
        outputCost: 5.0,
      },
    ],
    baseUrl: "https://generativelanguage.googleapis.com/v1beta/models/",
  },
};
```

**Cost note:** Prices are per 1M tokens. Gemini Flash has a free tier, making it ideal for users who don't want to spend money. Costs are displayed in the settings UI so users can make informed model choices.

## Main Interface

```javascript
// lib/ai-client.js

const SYSTEM_PROMPT = `...`; // (See AI_PROMPT.md for full prompt)

/**
 * Analyze extracted page text using the configured AI provider.
 *
 * @param {string} text - Extracted page text
 * @param {Object} settings - { provider, apiKey, model }
 * @returns {string} Raw AI response text
 * @throws {Error} On network failure, auth error, rate limit, etc.
 */
export async function analyze(text, settings) {
  const { provider, apiKey, model } = settings;

  // Validate inputs
  if (!apiKey || !apiKey.trim()) {
    throw new TCLError("NO_API_KEY", "API key is not configured.");
  }
  if (!provider || !PROVIDERS[provider]) {
    throw new TCLError("INVALID_PROVIDER", `Unknown provider: ${provider}`);
  }

  // Route to the correct provider implementation
  switch (provider) {
    case "openai":
      return await analyzeWithOpenAI(text, apiKey, model);
    case "anthropic":
      return await analyzeWithAnthropic(text, apiKey, model);
    case "gemini":
      return await analyzeWithGemini(text, apiKey, model);
    default:
      throw new TCLError(
        "INVALID_PROVIDER",
        `No handler for provider: ${provider}`,
      );
  }
}
```

## Custom Error Class

```javascript
class TCLError extends Error {
  constructor(code, message) {
    super(message);
    this.code = code;
    this.name = "TCLError";
  }
}
```

Standard error codes used throughout:

| Code               | Meaning                   | User-Facing Message                                               |
| ------------------ | ------------------------- | ----------------------------------------------------------------- |
| `NO_API_KEY`       | No API key configured     | "Please set your API key in Settings."                            |
| `INVALID_PROVIDER` | Unknown provider selected | "Invalid AI provider selected."                                   |
| `INVALID_KEY`      | API rejected the key      | "Your API key was rejected. Please check Settings."               |
| `RATE_LIMITED`     | Too many requests         | "API rate limit reached. Wait a moment and try again."            |
| `NETWORK_ERROR`    | Cannot reach the API      | "Could not reach the AI service. Check your internet connection." |
| `TIMEOUT`          | Request took too long     | "The AI service took too long to respond. Please try again."      |
| `RESPONSE_ERROR`   | API returned an error     | "The AI service returned an error: [details]"                     |
| `PARSE_ERROR`      | Could not parse response  | "The AI returned an unexpected format."                           |

## OpenAI Implementation

```javascript
async function analyzeWithOpenAI(text, apiKey, model) {
  const userMessage = buildUserMessage(text);

  const response = await fetch(PROVIDERS.openai.baseUrl, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${apiKey}`,
    },
    body: JSON.stringify({
      model: model,
      messages: [
        { role: "system", content: SYSTEM_PROMPT },
        { role: "user", content: userMessage },
      ],
      temperature: 0.1, // Low temperature for consistent, factual output
      max_tokens: 4000, // Enough for full JSON response
    }),
  });

  return handleOpenAIResponse(response);
}

function handleOpenAIResponse(response) {
  switch (response.status) {
    case 200:
      return response.json().then((data) => data.choices[0].message.content);
    case 401:
      throw new TCLError("INVALID_KEY", "Your OpenAI API key was rejected.");
    case 429:
      throw new TCLError(
        "RATE_LIMITED",
        "OpenAI rate limit reached. Wait a moment.",
      );
    case 500:
    case 502:
    case 503:
      throw new TCLError(
        "RESPONSE_ERROR",
        "OpenAI service is temporarily unavailable.",
      );
    default:
      throw new TCLError(
        "RESPONSE_ERROR",
        `OpenAI returned status ${response.status}.`,
      );
  }
}
```

## Anthropic Implementation

```javascript
async function analyzeWithAnthropic(text, apiKey, model) {
  const userMessage = buildUserMessage(text);

  const response = await fetch(PROVIDERS.anthropic.baseUrl, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "x-api-key": apiKey,
      "anthropic-version": "2023-06-01",
      "anthropic-dangerous-direct-browser-access": "true", // Required for browser-side requests
    },
    body: JSON.stringify({
      model: model,
      max_tokens: 4000,
      system: SYSTEM_PROMPT,
      messages: [{ role: "user", content: userMessage }],
    }),
  });

  return handleAnthropicResponse(response);
}

function handleAnthropicResponse(response) {
  switch (response.status) {
    case 200:
      return response.json().then((data) => data.content[0].text);
    case 401:
      throw new TCLError("INVALID_KEY", "Your Anthropic API key was rejected.");
    case 429:
      throw new TCLError(
        "RATE_LIMITED",
        "Anthropic rate limit reached. Wait a moment.",
      );
    default:
      throw new TCLError(
        "RESPONSE_ERROR",
        `Anthropic returned status ${response.status}.`,
      );
  }
}
```

**Note on Anthropic CORS:** Anthropic requires the `anthropic-dangerous-direct-browser-access` header when making requests from a browser (non-server) context. This is intentional — they want developers to use their SDK server-side in production. For our use case (user's own API key, direct browser request), this header is acceptable.

## Google Gemini Implementation

```javascript
async function analyzeWithGemini(text, apiKey, model) {
  const userMessage = buildUserMessage(text);
  const url = `${PROVIDERS.gemini.baseUrl}${model}:generateContent?key=${apiKey}`;

  const response = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      contents: [
        {
          parts: [{ text: SYSTEM_PROMPT + "\n\n" + userMessage }],
        },
      ],
      generationConfig: {
        temperature: 0.1,
        maxOutputTokens: 4000,
      },
    }),
  });

  return handleGeminiResponse(response);
}

function handleGeminiResponse(response) {
  switch (response.status) {
    case 200:
      return response
        .json()
        .then((data) => data.candidates[0].content.parts[0].text);
    case 400:
      throw new TCLError(
        "INVALID_KEY",
        "Your Gemini API key is invalid or the model name is wrong.",
      );
    case 403:
      throw new TCLError(
        "INVALID_KEY",
        "Your Gemini API key does not have permission.",
      );
    case 429:
      throw new TCLError(
        "RATE_LIMITED",
        "Gemini rate limit reached. Wait a moment.",
      );
    default:
      throw new TCLError(
        "RESPONSE_ERROR",
        `Gemini returned status ${response.status}.`,
      );
  }
}
```

**Note on Gemini API key in URL:** Gemini uses the API key as a query parameter rather than a header. This is their standard API design. The key is transmitted over HTTPS, so it's encrypted in transit. Since this is the user's own key entered intentionally, this is acceptable.

## Request Timeout

All requests should have a timeout to prevent hanging indefinitely on slow networks or unresponsive APIs:

```javascript
async function fetchWithTimeout(url, options, timeoutMs = 60000) {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(url, {
      ...options,
      signal: controller.signal,
    });
    return response;
  } catch (error) {
    if (error.name === "AbortError") {
      throw new TCLError("TIMEOUT", "The AI service took too long to respond.");
    }
    throw new TCLError(
      "NETWORK_ERROR",
      "Could not reach the AI service. Check your internet connection.",
    );
  } finally {
    clearTimeout(timeoutId);
  }
}
```

Each provider function uses `fetchWithTimeout` instead of raw `fetch`.

## Helper: Build User Message

```javascript
function buildUserMessage(text) {
  return `Analyze the following text from a webpage. Extract all findings, assign importance levels, and calculate a risk score.\n\n---\n${text}\n---`;
}
```

## Cost Estimation Helper

For displaying estimated cost to the user before analysis:

```javascript
function estimateCost(text, provider, model) {
  const providerConfig = PROVIDERS[provider];
  const modelConfig = providerConfig.models.find((m) => m.id === model);
  if (!modelConfig) return null;

  // Rough token estimation: 4 chars per token for English
  const inputTokens = Math.ceil((SYSTEM_PROMPT.length + text.length) / 4);
  const estimatedOutputTokens = 2000; // Approximate response size

  const inputCost = (inputTokens / 1000000) * modelConfig.inputCost;
  const outputCost = (estimatedOutputTokens / 1000000) * modelConfig.outputCost;

  return {
    inputTokens,
    estimatedOutputTokens,
    totalCost: inputCost + outputCost,
  };
}
```

This estimate is shown on the dashboard before the user clicks "Analyze" so they know roughly how much the analysis will cost them.
