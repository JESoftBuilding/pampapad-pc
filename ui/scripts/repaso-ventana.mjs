// Maneja la ventana real de PampaPad por el protocolo de depuración de WebView2 para el repaso de la etapa 7 (docs/manual-test.md): la
// copia de desarrollo abierta con WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222 y PAMPAPAD_LIBRARY apuntando a una
// copia de la biblioteca de ejemplo. Imprime lo que la página mostró; lo que quedó en el disco lo mira el bloque de PowerShell que lo llama.
// Uso, desde cualquier carpeta: node ui/scripts/repaso-ventana.mjs <tecla|atrasado|accion> [puerto]
import { chromium } from "@playwright/test";

const [step = "tecla", port = "9222"] = process.argv.slice(2);
const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
const page = browser
  .contexts()
  .flatMap((context) => context.pages())
  .find((candidate) => candidate.url().includes("index.html"));
if (page === undefined) {
  console.error("No hay ninguna página de PampaPad en ese puerto.");
  process.exit(2);
}

const say = (line) => console.log(`${new Date().toISOString().slice(11, 23)} ${line}`);

async function go(screen) {
  // Como texto y no como función: ESLint no conoce window ni location fuera de src/ (no-undef), y la página sí.
  await page.evaluate(`location.hash = ${JSON.stringify(`#/${screen}`)}`);
  await page.getByRole("heading", { level: 1 }).waitFor({ timeout: 10_000 });
  say(`pantalla: ${await page.getByRole("heading", { level: 1 }).innerText()}`);
}

async function saveAndTell() {
  await page.getByRole("button", { name: "Guardar" }).click();
  const outcome = page.getByText("Guardado.").or(page.getByRole("alert").filter({ hasText: "No se guardó" }));
  await outcome.first().waitFor({ timeout: 15_000 });
  say(`guardar: ${(await outcome.first().innerText()).replace(/\s+/g, " ")}`);
}

if (step === "tecla") {
  // Teclas: la tecla 1 de grabacion pasa a "Prueba E7" con marca-short, y se graba Ctrl+Shift+Alt+F2 (libre en la biblioteca de ejemplo) con el teclado inyectado por CDP
  // (la copia de desarrollo no registra atajos: la combinación llega a la página sin suspender nada de verdad).
  await go("keys");
  await page.getByRole("button", { name: "Tecla 1", exact: true }).click();
  await page.getByLabel("Etiqueta").fill("Prueba E7");
  await page.getByLabel("Qué hace").selectOption("marca-short");
  await page.getByRole("button", { name: "Grabar atajo" }).click();
  await page.getByText("Apretá la combinación").waitFor({ timeout: 5_000 });
  await page.keyboard.press("Control+Shift+Alt+F2");
  say(`atajo: ${await page.getByLabel("Atajo de teclado").inputValue()}`);
  say(`tecla 1: ${(await page.getByRole("button", { name: "Tecla 1", exact: true }).innerText()).replace(/\s+/g, " ")}`);
  await saveAndTell();
} else if (step === "atrasado") {
  // El archivo cambió en el disco (lo editó el bloque de PowerShell): guardar tiene que fallar con config_changed_on_disk y recargar tiene que
  // traer lo del disco.
  await go("keys");
  await page.getByRole("button", { name: "Tecla 1", exact: true }).click();
  await page.getByLabel("Etiqueta").fill("Pisada");
  await saveAndTell();
  await page.getByRole("button", { name: "Recargar biblioteca" }).click();
  await page.getByLabel("Etiqueta").waitFor({ timeout: 10_000 });
  say(`después de recargar, etiqueta: ${await page.getByLabel("Etiqueta").inputValue()}`);
} else if (step === "accion") {
  // Acciones: una acción nueva con wait.time y obs.marker, y Guardar.
  await go("actions");
  await page.getByLabel("Nombre de la acción nueva").fill("Prueba E7 acción");
  await page.getByRole("button", { name: "Crear acción" }).click();
  await page.getByLabel("Tipo de paso").selectOption("wait.time");
  await page.getByRole("button", { name: "Agregar paso" }).click();
  await page.getByLabel("Cuánto, en milisegundos").fill("500");
  await page.getByLabel("Tipo de paso").selectOption("obs.marker");
  await page.getByRole("button", { name: "Agregar paso" }).click();
  await page.getByLabel("Tipo de marca").selectOption("short");
  say(`pasos: ${(await page.getByRole("list", { name: "Pasos" }).innerText()).replace(/\s+/g, " ")}`);
  await saveAndTell();
} else {
  console.error(`No existe el paso ${step}: tecla, atrasado o accion.`);
  process.exit(2);
}

// Sin browser.close(): con connectOverCDP podría cerrar el navegador de la ventana. Al salir, se corta sólo la conexión.
process.exit(0);
