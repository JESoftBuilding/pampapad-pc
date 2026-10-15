import { fileURLToPath, URL } from "node:url";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// La página armada se sirve desde la carpeta ui\ de la instalación con el host virtual de WebView2 (01 §5): rutas relativas, sin servidor.
// En la copia de desarrollo, con PAMPAPAD_UI_DEV, el programa navega a este servidor (05 §10).
export default defineConfig({
  base: "./",
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  server: { port: 5173, strictPort: true },
  build: { outDir: "dist", emptyOutDir: true },
  test: { include: ["src/**/*.test.ts"], environment: "node" },
});
