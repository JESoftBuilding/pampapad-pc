import js from "@eslint/js";
import reactHooks from "eslint-plugin-react-hooks";
import { defineConfig } from "eslint/config";
import tseslint from "typescript-eslint";

// Las dos barreras de la página (01 §1, regla 4): chrome.webview sólo dentro de src/bridge/, y model/ no importa de nadie.
// Que realBridge y fakeBridge implementen el mismo tipo lo exige el compilador, no ESLint.
export default defineConfig(
  { ignores: ["dist", "node_modules", "playwright-report", "test-results"] },
  js.configs.recommended,
  tseslint.configs.recommended,
  {
    files: ["**/*.{ts,tsx}"],
    plugins: { "react-hooks": reactHooks },
    rules: {
      "react-hooks/rules-of-hooks": "error",
      "react-hooks/exhaustive-deps": "error",
    },
  },
  {
    files: ["src/**/*.{ts,tsx}"],
    ignores: ["src/bridge/**"],
    rules: {
      "no-restricted-syntax": [
        "error",
        { selector: "MemberExpression[property.name='webview']", message: "chrome.webview se usa sólo dentro de src/bridge/ (01 §1)." },
      ],
    },
  },
  {
    files: ["src/model/**/*.ts"],
    rules: {
      "no-restricted-imports": ["error", { patterns: [{ group: ["@/*", "../*"], message: "model/ no importa de nadie (01 §1)." }] }],
    },
  },
  {
    files: ["src/screens/**/*.{ts,tsx}"],
    rules: {
      "no-restricted-imports": ["error", { patterns: [{ group: ["@/App", "@/Frame", "@/main"], message: "screens/ importa de model/, bridge/ y ui/ (01 §1)." }] }],
    },
  },
  {
    files: ["scripts/**/*.mjs"],
    languageOptions: { globals: { process: "readonly", console: "readonly" } },
  },
);
