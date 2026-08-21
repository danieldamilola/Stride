/**
 * @file ai-client.js
 * @description Provider-agnostic AI API wrapper for T&C Lens.
 * @module lib/ai-client
 */

import { SYSTEM_PROMPT, buildUserMessage } from "./prompt.js";
import {
  PROVIDERS,
  normalizeGeminiModelId,
  normalizeOpenAICompatibleModel,
  isLikelyChatModel,
} from "./providers.js";

class TCLError extends Error {
  constructor(code, message) {
    super(message);
    this.code = code;
    this.name = "TCLError";
  }
}

/**
 * Analyze extracted page text using the configured AI provider.
 *
 * @param {string} text - Extracted text content from the target page.
 * @param {Object} settings - User AI configuration.
 * @param {string} settings.provider - Provider ID.
 * @param {string} settings.apiKey - API key for the provider.
 * @param {string} settings.model - Model ID.
 * @param {string} url - Target page URL.
 * @returns {Promise<string>} Raw AI response text.
 * @throws {TCLError} When validation, network, auth, or provider errors occur.
 */
export async function analyze(text, settings, url) {
  const { provider, apiKey, model } = settings;

  if (!apiKey || !apiKey.trim()) {
    throw new TCLError("NO_API_KEY", "API key is not configured.");
  }

  if (!provider || !PROVIDERS[provider]) {
    throw new TCLError("INVALID_PROVIDER", `Unknown provider: ${provider}`);
  }

  switch (provider) {
    case "anthropic":
      return await analyzeWithAnthropic(text, apiKey, model, url);
    case "gemini":
      return await analyzeWithGemini(text, apiKey, model, url);
    default:
      if (PROVIDERS[provider].apiType === "openai-compatible") {
        return await analyzeWithOpenAICompatible(
          text,
          apiKey,
          model,
          PROVIDERS[provider],
          url,
        );
      }

      throw new TCLError(
        "INVALID_PROVIDER",
        `No handler for provider: ${provider}`,
      );
  }
}

/**
 * Estimate AI request cost for the selected model.
 * Note: This is a rough estimate. Actual cost depends on page length and
 * model-specific tokenization. The ~4 chars/token heuristic is approximate.
 *
 * @param {string} text - Text to analyze (pass actual extracted text for accuracy).
 * @param {string} provider - Provider ID.
 * @param {string} model - Model ID.
 * @returns {Object|null} Estimated token and cost details.
 */
export function estimateCost(text, provider, model) {
  const providerConfig = PROVIDERS[provider];
  const modelConfig = providerConfig?.models.find((item) => item.id === model);

  if (!modelConfig) return null;

  // ~4 chars per token is a rough average. Real tokenization varies by model.
  const inputTokens = Math.ceil((SYSTEM_PROMPT.length + text.length) / 4);
  const estimatedOutputTokens = 2000;
  const inputCost = (inputTokens / 1000000) * modelConfig.inputCost;
  const outputCost = (estimatedOutputTokens / 1000000) * modelConfig.outputCost;

  return {
    inputTokens,
    estimatedOutputTokens,
    totalCost: inputCost + outputCost,
    isEstimate: true,
  };
}

/**
 * Get available AI providers and models.
 *
 * @returns {Object} Provider configuration map.
 */
export function getProviders() {
  return PROVIDERS;
}

/**
 * List available models for a provider when the provider exposes a useful model API.
 *
 * @param {string} provider - Provider ID.
 * @param {string} apiKey - API key for model discovery.
 * @returns {Promise<Array>} Models available for analysis.
 * @throws {TCLError} When model discovery fails.
 */
export async function listAvailableModels(provider, apiKey) {
  if (!provider || !PROVIDERS[provider]) {
    throw new TCLError("INVALID_PROVIDER", `Unknown provider: ${provider}`);
  }

  if (!apiKey || !apiKey.trim()) {
    throw new TCLError("NO_API_KEY", "API key is not configured.");
  }

  if (provider === "gemini") {
    return await listGeminiModels(apiKey);
  }

  if (PROVIDERS[provider].apiType === "openai-compatible") {
    return await listOpenAICompatibleModels(PROVIDERS[provider], apiKey);
  }

  return PROVIDERS[provider].models;
}

/**
 * Prepare page text for the selected model context window.
 *
 * Keeps the beginning of the document - where definitions, scope, and the
 * most important clauses typically appear in legal agreements.
 *
 * @param {string} rawText - Raw extracted page text.
 * @param {number} maxTokens - Maximum approximate input tokens.
 * @returns {{ text: string, wasTruncated: boolean }} Prepared text and flag.
 */
export function prepareTextForAI(rawText, maxTokens = 8000) {
  const maxChars = maxTokens * 4;
  const text = (rawText || "").trim();
  const wasTruncated = text.length > maxChars;

  const prepared = wasTruncated
    ? `${text.slice(0, maxChars)}\n\n[Note: Document was truncated - ${Math.round(text.length / 1000)}k characters total, showing first ${Math.round(maxChars / 1000)}k.]`
    : text;

  return { text: prepared, wasTruncated };
}

/* ─── Model listing ──────────────────────────────────────────── */

async function listGeminiModels(apiKey) {
  const response = await fetchWithRetry(
    `${PROVIDERS.gemini.baseUrl.replace(/\/$/, "")}?key=${encodeURIComponent(apiKey)}`,
    {
      method: "GET",
      headers: { "Content-Type": "application/json" },
    },
  );

  if (response.status !== 200) {
    await throwProviderError(response, "Gemini");
  }

  const data = await response.json();

  return (data.models || [])
    .filter((model) =>
      model.supportedGenerationMethods?.includes("generateContent"),
    )
    .map((model) => ({
      id: normalizeGeminiModelId(model.name),
      name: model.displayName || normalizeGeminiModelId(model.name),
      maxTokens: model.inputTokenLimit || 1000000,
      inputCost: 0,
      outputCost: 0,
    }));
}

async function listOpenAICompatibleModels(providerConfig, apiKey) {
  if (!providerConfig.modelsUrl) return providerConfig.models;

  const response = await fetchWithRetry(providerConfig.modelsUrl, {
    method: "GET",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${apiKey}`,
      ...(providerConfig.headers || {}),
    },
  });

  if (response.status !== 200) {
    await throwProviderError(response, providerConfig.name);
  }

  const data = await response.json();
  const models = Array.isArray(data.data) ? data.data : data.models || [];
  const compatibleModels = models
    .map(normalizeOpenAICompatibleModel)
    .filter((model) => isLikelyChatModel(model.id));

  return compatibleModels.length > 0 ? compatibleModels : providerConfig.models;
}

/* ─── Provider-specific analysis ─────────────────────────────── */

async function analyzeWithOpenAICompatible(
  text,
  apiKey,
  model,
  providerConfig,
  url,
) {
  const response = await fetchWithRetry(providerConfig.baseUrl, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${apiKey}`,
      ...(providerConfig.headers || {}),
    },
    body: JSON.stringify({
      model,
      messages: [
        { role: "system", content: SYSTEM_PROMPT },
        { role: "user", content: buildUserMessage(text, url) },
      ],
      temperature: 0.1,
      max_tokens: providerConfig.maxOutputTokens || 4000,
    }),
  });

  return await handleOpenAICompatibleResponse(response, providerConfig.name);
}

async function analyzeWithAnthropic(text, apiKey, model, url) {
  const response = await fetchWithRetry(PROVIDERS.anthropic.baseUrl, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "x-api-key": apiKey,
      "anthropic-version": "2023-06-01",
      "anthropic-dangerous-direct-browser-access": "true",
    },
    body: JSON.stringify({
      model,
      max_tokens: PROVIDERS.anthropic.maxOutputTokens || 4000,
      system: SYSTEM_PROMPT,
      messages: [{ role: "user", content: buildUserMessage(text, url) }],
    }),
  });

  return await handleAnthropicResponse(response);
}

async function analyzeWithGemini(text, apiKey, model, url) {
  const modelId = normalizeGeminiModelId(model);
  const endpointUrl = `${PROVIDERS.gemini.baseUrl}${modelId}:generateContent?key=${encodeURIComponent(apiKey)}`;
  const response = await fetchWithRetry(endpointUrl, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      contents: [
        {
          parts: [{ text: `${SYSTEM_PROMPT}\n\n${buildUserMessage(text, url)}` }],
        },
      ],
      generationConfig: {
        temperature: 0.1,
        maxOutputTokens: 4000,
      },
    }),
  });

  return await handleGeminiResponse(response);
}

/* ─── Network helpers ────────────────────────────────────────── */

/**
 * Fetch with timeout and automatic retry for transient failures.
 * Retries once on 429 (rate limit) and 5xx (server errors) with
 * exponential backoff.
 *
 * @param {string} url - Request URL.
 * @param {Object} options - Fetch options.
 * @param {number} [timeoutMs=60000] - Timeout in milliseconds.
 * @param {number} [maxRetries=1] - Maximum retry attempts.
 * @returns {Promise<Response>} Fetch response.
 * @throws {TCLError} On timeout or network failure.
 */
async function fetchWithRetry(url, options, timeoutMs = 60000, maxRetries = 1) {
  let lastError;

  for (let attempt = 0; attempt <= maxRetries; attempt++) {
    try {
      const response = await fetchWithTimeout(url, options, timeoutMs);

      // Retry on transient errors (rate limit or server errors)
      const isRetryable =
        response.status === 429 ||
        (response.status >= 500 && response.status < 600);

      if (isRetryable && attempt < maxRetries) {
        const delayMs = Math.min(1000 * 2 ** attempt, 4000);
        await delay(delayMs);
        continue;
      }

      return response;
    } catch (error) {
      lastError = error;

      // Only retry on network errors, not on timeouts or validation errors
      if (error.code === "NETWORK_ERROR" && attempt < maxRetries) {
        const delayMs = Math.min(1000 * 2 ** attempt, 4000);
        await delay(delayMs);
        continue;
      }

      throw error;
    }
  }

  throw lastError;
}

async function fetchWithTimeout(url, options, timeoutMs = 60000) {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

  try {
    return await fetch(url, { ...options, signal: controller.signal });
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

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/* ─── Response handlers ──────────────────────────────────────── */

async function handleOpenAICompatibleResponse(response, providerName) {
  if (response.status === 200) {
    const data = await response.json();
    return data.choices?.[0]?.message?.content || data.choices?.[0]?.text || "";
  }

  await throwProviderError(response, providerName);
}

async function handleAnthropicResponse(response) {
  if (response.status === 200) {
    const data = await response.json();
    return data.content?.[0]?.text || "";
  }

  await throwProviderError(response, "Anthropic");
}

async function handleGeminiResponse(response) {
  if (response.status === 200) {
    const data = await response.json();
    return data.candidates?.[0]?.content?.parts?.[0]?.text || "";
  }

  await throwProviderError(response, "Gemini");
}

/* ─── Error handling ─────────────────────────────────────────── */

async function throwProviderError(response, providerName) {
  const details = await getErrorDetails(response);

  if (response.status === 401 || response.status === 403) {
    throw new TCLError(
      "INVALID_KEY",
      `Your ${providerName} API key was rejected.`,
    );
  }

  if (response.status === 400 && providerName === "Gemini") {
    throw new TCLError(
      "INVALID_KEY",
      "Your Gemini API key is invalid or the model name is wrong.",
    );
  }

  if (response.status === 429) {
    throw new TCLError(
      "RATE_LIMITED",
      `${providerName} rate limit reached. Wait a moment.`,
    );
  }

  if ([500, 502, 503, 504].includes(response.status)) {
    throw new TCLError(
      "RESPONSE_ERROR",
      `${providerName} service is temporarily unavailable.`,
    );
  }

  throw new TCLError(
    "RESPONSE_ERROR",
    `${providerName} returned status ${response.status}. ${details}`.trim(),
  );
}

async function getErrorDetails(response) {
  try {
    const data = await response.json();
    return data.error?.message || data.message || "";
  } catch {
    return "";
  }
}

export { TCLError };

