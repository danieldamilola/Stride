import js from "@eslint/js";

export default [
  js.configs.recommended,
  {
    languageOptions: {
      ecmaVersion: "latest",
      sourceType: "module",
      globals: {
        // Browser globals
        document: "readonly",
        window: "readonly",
        navigator: "readonly",
        console: "readonly",
        setTimeout: "readonly",
        clearTimeout: "readonly",
        fetch: "readonly",
        confirm: "readonly",
        alert: "readonly",
        URL: "readonly",
        Blob: "readonly",
        AbortController: "readonly",
        DOMParser: "readonly",
        Intl: "readonly",
        requestAnimationFrame: "readonly",
        globalThis: "readonly",
        // Chrome extension globals
        chrome: "readonly",
      },
    },
    rules: {
      "no-unused-vars": ["warn", { argsIgnorePattern: "^_" }],
      "no-console": ["warn", { allow: ["warn", "error"] }],
      "prefer-const": "error",
      eqeqeq: ["error", "always"],
      "no-var": "error",
    },
  },
  {
    ignores: ["node_modules/", "*.zip"],
  },
];
