/**
 * @file providers.js
 * @description AI provider registry and model normalization for T&C Lens.
 * @module lib/providers
 */

// Hosted T&C Lens AI endpoint.
// Set TCLENS_DEV to true while testing against a local tclens-web dev server
// (http://localhost:3000 is already allowed by the manifest CSP), then flip it
// back to false before shipping.
const TCLENS_DEV = false;
const TCLENS_ORIGIN = TCLENS_DEV
  ? "http://localhost:3000"
  : "https://www.tclens.me";

export const PROVIDERS = {
  tclens: {
    name: "T&C Lens AI",
    models: [
      {
        id: "tclens-hosted",
        name: "Hosted AI",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      }
    ],
    baseUrl: `${TCLENS_ORIGIN}/api/analyze`,
    creditsUrl: `${TCLENS_ORIGIN}/api/credits/balance`,
  },
  openai: {
    name: "OpenAI",
    apiType: "openai-compatible",
    models: [
      {
        id: "gpt-4o",
        name: "GPT-4o",
        maxTokens: 128000,
        inputCost: 2.5,
        outputCost: 10,
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
    modelsUrl: "https://api.openai.com/v1/models",
  },
  groq: {
    name: "Groq",
    apiType: "openai-compatible",
    analysisMaxInputTokens: 2500,
    maxOutputTokens: 1500,
    models: [
      {
        id: "llama-3.3-70b-versatile",
        name: "Llama 3.3 70B Versatile",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
      {
        id: "llama-3.1-8b-instant",
        name: "Llama 3.1 8B Instant",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
      {
        id: "deepseek-r1-distill-llama-70b",
        name: "DeepSeek R1 Distill Llama 70B",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
    ],
    baseUrl: "https://api.groq.com/openai/v1/chat/completions",
    modelsUrl: "https://api.groq.com/openai/v1/models",
  },
  deepseek: {
    name: "DeepSeek",
    apiType: "openai-compatible",
    models: [
      {
        id: "deepseek-chat",
        name: "DeepSeek Chat",
        maxTokens: 64000,
        inputCost: 0,
        outputCost: 0,
      },
      {
        id: "deepseek-reasoner",
        name: "DeepSeek Reasoner",
        maxTokens: 64000,
        inputCost: 0,
        outputCost: 0,
      },
    ],
    baseUrl: "https://api.deepseek.com/chat/completions",
    modelsUrl: "https://api.deepseek.com/models",
  },
  mistral: {
    name: "Mistral AI",
    apiType: "openai-compatible",
    models: [
      {
        id: "mistral-large-latest",
        name: "Mistral Large",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
      {
        id: "mistral-small-latest",
        name: "Mistral Small",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
    ],
    baseUrl: "https://api.mistral.ai/v1/chat/completions",
    modelsUrl: "https://api.mistral.ai/v1/models",
  },
  openrouter: {
    name: "OpenRouter",
    apiType: "openai-compatible",
    models: [
      {
        id: "google/gemini-2.5-flash",
        name: "Gemini 2.5 Flash",
        maxTokens: 1000000,
        inputCost: 0,
        outputCost: 0,
      },
      {
        id: "deepseek/deepseek-chat-v3-0324",
        name: "DeepSeek Chat V3",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
      {
        id: "meta-llama/llama-3.3-70b-instruct",
        name: "Llama 3.3 70B Instruct",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
    ],
    baseUrl: "https://openrouter.ai/api/v1/chat/completions",
    modelsUrl: "https://openrouter.ai/api/v1/models",
    headers: {
      "HTTP-Referer": "https://tc-lens.local",
      "X-Title": "T&C Lens",
    },
  },
  xai: {
    name: "xAI",
    apiType: "openai-compatible",
    models: [
      {
        id: "grok-3",
        name: "Grok 3",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
      {
        id: "grok-3-mini",
        name: "Grok 3 Mini",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
    ],
    baseUrl: "https://api.x.ai/v1/chat/completions",
    modelsUrl: "https://api.x.ai/v1/models",
  },
  together: {
    name: "Together AI",
    apiType: "openai-compatible",
    models: [
      {
        id: "meta-llama/Llama-3.3-70B-Instruct-Turbo",
        name: "Llama 3.3 70B Turbo",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
      {
        id: "deepseek-ai/DeepSeek-V3",
        name: "DeepSeek V3",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
      {
        id: "Qwen/Qwen2.5-72B-Instruct-Turbo",
        name: "Qwen 2.5 72B Turbo",
        maxTokens: 128000,
        inputCost: 0,
        outputCost: 0,
      },
    ],
    baseUrl: "https://api.together.xyz/v1/chat/completions",
    modelsUrl: "https://api.together.xyz/v1/models",
  },
  anthropic: {
    name: "Anthropic",
    models: [
      {
        id: "claude-sonnet-4-20250514",
        name: "Claude Sonnet 4",
        maxTokens: 200000,
        inputCost: 3,
        outputCost: 15,
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
        id: "gemini-2.5-flash",
        name: "Gemini 2.5 Flash",
        maxTokens: 1000000,
        inputCost: 0,
        outputCost: 0,
      },
      {
        id: "gemini-2.5-pro",
        name: "Gemini 2.5 Pro",
        maxTokens: 2000000,
        inputCost: 1.25,
        outputCost: 5,
      },
      {
        id: "gemini-2.0-flash",
        name: "Gemini 2.0 Flash",
        maxTokens: 1000000,
        inputCost: 0,
        outputCost: 0,
      },
    ],
    baseUrl: "https://generativelanguage.googleapis.com/v1beta/models/",
  },
};

/**
 * Normalize a Gemini model ID by stripping the "models/" prefix.
 *
 * @param {string} model - Raw model identifier.
 * @returns {string} Normalized model ID.
 */
export function normalizeGeminiModelId(model) {
  return String(model || "gemini-2.5-flash").replace(/^models\//, "");
}

/**
 * Normalize an OpenAI-compatible model object to a standard shape.
 *
 * @param {Object} model - Raw model object from provider API.
 * @returns {Object} Normalized model with id, name, maxTokens, inputCost, outputCost.
 */
export function normalizeOpenAICompatibleModel(model) {
  const id = String(model.id || model.name || "").replace(/^models\//, "");

  return {
    id,
    name: model.display_name || model.displayName || model.name || id,
    maxTokens:
      model.context_length ||
      model.contextLength ||
      model.max_context_length ||
      128000,
    inputCost: Number(model.pricing?.prompt || 0),
    outputCost: Number(model.pricing?.completion || 0),
  };
}

/**
 * Check whether a model ID is likely a chat/completion model.
 *
 * @param {string} modelId - Model identifier to check.
 * @returns {boolean} True when the model is likely usable for chat.
 */
export function isLikelyChatModel(modelId) {
  const id = modelId.toLowerCase();
  const rejectedTerms = [
    "embedding",
    "embed",
    "whisper",
    "tts",
    "dall-e",
    "image",
    "audio",
    "moderation",
    "rerank",
    "babbage",
    "davinci",
  ];

  return Boolean(modelId) && !rejectedTerms.some((term) => id.includes(term));
}

