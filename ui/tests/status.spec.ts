import { expect, test } from "@playwright/test";

// Lo que la página deja en window.pampapadFake cuando corre con el puente falso (src/bridge/bridge.ts).
type Fake = {
  calls: Array<{ name: string; data: unknown }>;
  emit(event: string, data: unknown, seq: number): void;
  sampleState(): { seq: number; obs: Record<string, unknown> } & Record<string, unknown>;
};

test.beforeEach(async ({ page }) => {
  await page.goto("/#/status");
  await expect(page.getByLabel("Estado de un vistazo")).toBeVisible();
});

test("abre en Estado con la foto de las muestras", async ({ page }) => {
  const strip = page.getByLabel("Estado de un vistazo");

  await expect(page.getByRole("heading", { level: 1, name: "Estado" })).toBeVisible();
  await expect(strip.getByText("OBS conectado")).toBeVisible();
  await expect(strip.getByText("Grabando 0:12:34")).toBeVisible();
  await expect(strip.getByText("Consola COM20: conectada")).toBeVisible();
  await expect(page.getByText("Versión 0.2.1")).toBeVisible();
  await expect(page.getByText("El chequeo previo llega en la etapa 5")).toBeVisible();
  await expect(page.getByRole("list", { name: "Atajos que rechazó Windows" })).toContainText("Ctrl+Shift+Alt+F6");
});

test("un evento de estado más nuevo cambia la pantalla y uno más viejo no", async ({ page }) => {
  const strip = page.getByLabel("Estado de un vistazo");

  await page.evaluate(() => {
    const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
    const state = fake.sampleState();
    fake.emit("state", { ...state, seq: state.seq + 1, obs: { ...state.obs, link: "closed", recording: "stopped" } }, state.seq + 1);
  });
  await expect(strip.getByText("Sin OBS: no está abierto")).toBeVisible();

  await page.evaluate(() => {
    const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
    const state = fake.sampleState();
    fake.emit("state", { ...state, seq: state.seq - 1 }, state.seq - 1);
  });
  await expect(strip.getByText("Sin OBS: no está abierto")).toBeVisible();
  await expect(strip.getByText("OBS conectado")).toHaveCount(0);
});

test("Cancelar pide action.cancel con el runId de esa acción", async ({ page }) => {
  await page.getByRole("button", { name: "Cancelar", exact: true }).click();

  await expect(page.getByRole("status")).toHaveText("Se pidió cancelar 1 acción.");
  const calls = await page.evaluate(() => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === "action.cancel"));
  expect(calls).toEqual([{ name: "action.cancel", data: { runId: "0f8fad5b-d9cb-469f-a165-70867728950e" } }]);
});

test("Cancelar todo pide action.cancel con all", async ({ page }) => {
  await page.getByRole("button", { name: "Cancelar todo" }).click();

  const calls = await page.evaluate(() => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === "action.cancel"));
  expect(calls).toEqual([{ name: "action.cancel", data: { all: true } }]);
});

test("el registro filtra por nivel y suma las líneas que llegan en vivo", async ({ page }) => {
  await expect(page.getByText("enlace con OBS: Ready (lectura inicial completa)")).toBeVisible();

  await page.getByLabel("Nivel").selectOption("warning");
  await expect(page.getByText("enlace con OBS: Ready (lectura inicial completa)")).toHaveCount(0);
  await expect(page.getByText("marca-fin falló en el paso 1 (obs.marker): Estás en pausa: la marca no entró.")).toBeVisible();

  await page.getByLabel("Nivel").selectOption("info");
  await page.evaluate(() => {
    const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
    fake.emit("log", { at: "2026-10-14T23:31:00-03:00", level: "info", module: "prueba", message: "una línea en vivo" }, 43);
  });
  await expect(page.getByText("una línea en vivo")).toBeVisible();
});

test("una línea nueva en vivo no vuelve a montar las filas ya pintadas: la fila y el foco siguen", async ({ page }) => {
  const button = page.getByRole("button", { name: "sólo esta ejecución" }).first();
  await button.focus();
  await expect(button).toBeFocused();
  const buttonHandle = await button.elementHandle();
  const rowHandle = await button.locator("xpath=ancestor::li").elementHandle();
  expect(buttonHandle).not.toBeNull();
  expect(rowHandle).not.toBeNull();

  await page.evaluate(() => {
    const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
    fake.emit("log", { at: "2026-10-14T23:31:00-03:00", level: "info", module: "prueba", message: "una línea en vivo" }, 43);
  });
  await expect(page.getByText("una línea en vivo")).toBeVisible();

  expect(await rowHandle?.evaluate((row) => row.isConnected)).toBe(true);
  expect(await buttonHandle?.evaluate((node) => node === document.activeElement)).toBe(true);

  // Filtrar por esa ejecución también reordena la lista: las filas que quedan son las mismas.
  await button.click();
  expect(await rowHandle?.evaluate((row) => row.isConnected)).toBe(true);
});

test("las pantallas que todavía no existen dicen en qué etapa llegan", async ({ page }) => {
  await page.getByRole("navigation", { name: "Pantallas" }).getByRole("link", { name: /Scripts/ }).click();

  await expect(page).toHaveURL(/#\/scripts$/);
  await expect(page.getByRole("heading", { level: 1, name: "Scripts" })).toBeVisible();
  await expect(page.getByText("Esta pantalla llega en la etapa 8.")).toBeVisible();
});

test("un problema de una tecla lleva a Teclas, con su página y su posición", async ({ page }) => {
  await page.getByRole("link", { name: "pages[0].keys[1].hotkey" }).click();

  await expect(page).toHaveURL(/#\/keys\?page=grabacion&position=/);
  await expect(page.getByRole("heading", { level: 1, name: "Teclas" })).toBeVisible();
});

test("manda window.ready una vez, con la foto ya pedida", async ({ page }) => {
  const ready = await page.evaluate(() => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === "window.ready").length);

  expect(ready).toBe(1);
});
