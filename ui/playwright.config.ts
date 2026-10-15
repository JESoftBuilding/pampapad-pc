import { defineConfig } from "@playwright/test";

const port = 5174;

// Playwright contra fakeBridge (01 §7): la página corre en el servidor de Vite, en un navegador común, sin el programa.
// channel msedge: el Edge que ya está en la PC y en el CI; no se baja ningún navegador. Otro puerto que el de desarrollo (5173).
export default defineConfig({
  testDir: "tests",
  fullyParallel: false,
  workers: 1,
  forbidOnly: process.env.CI !== undefined,
  retries: 0,
  reporter: "list",
  use: { baseURL: `http://localhost:${port}`, channel: "msedge" },
  webServer: {
    command: `pnpm exec vite --port ${port} --strictPort`,
    url: `http://localhost:${port}`,
    reuseExistingServer: false,
    timeout: 60_000,
  },
});
