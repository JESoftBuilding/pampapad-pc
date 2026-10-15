// Lee la ventana real de PampaPad por el protocolo de depuración de WebView2: imprime la dirección y el texto de la página y saca
// una captura. Sólo para el repaso, con la copia de desarrollo abierta con
// WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222.
// Uso, desde cualquier carpeta: node ui/scripts/capture-window.mjs <captura.png> [puerto]
import { chromium } from "@playwright/test";

const [out = "ventana.png", port = "9222"] = process.argv.slice(2);
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
const page = browser
  .contexts()
  .flatMap((context) => context.pages())
  .find((candidate) => candidate.url().includes("index.html"));
if (page === undefined) {
  console.error("No hay ninguna página de PampaPad en ese puerto.");
  process.exit(2);
}

console.log(page.url());
console.log(await page.locator("body").innerText());
try {
  await page.screenshot({ path: out, timeout: 10_000 });
  console.log(`Captura: ${out}`);
} catch (error) {
  // Con la sesión bloqueada o la ventana tapada, la captura puede no salir: el texto de arriba alcanza como evidencia.
  console.error(`Sin captura: ${error.message}`);
}

// Sin browser.close(): con connectOverCDP podría cerrar el navegador de la ventana. Al salir, se corta sólo la conexión.
process.exit(0);
