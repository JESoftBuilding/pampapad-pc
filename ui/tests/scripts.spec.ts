import { expect, test, type Page } from "@playwright/test";

type Fake = {
  calls: Array<{ name: string; data: unknown }>;
  emit(event: string, data: unknown, seq: number): void;
  answer(name: string, reply: { ok: true; data: unknown } | { ok: false; error: { code: string; message: string } }): void;
  sampleState(): Record<string, unknown> & { seq: number; obs: Record<string, unknown> };
};

// El runId de la muestra de scripts.test (Contract.cs).
const runId = "0f8fad5b-d9cb-469f-a165-70867728950e";

const calls = (page: Page, name: string) => page.evaluate((wanted) => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === wanted).map((call) => call.data), name);
const emit = (page: Page, event: string, data: unknown, seq: number) =>
  page.evaluate(([name, payload, number]) => (window as unknown as { pampapadFake: Fake }).pampapadFake.emit(name as string, payload, number as number), [event, data, seq]);
const answer = (page: Page, name: string, reply: unknown) =>
  page.evaluate(([wanted, value]) => (window as unknown as { pampapadFake: Fake }).pampapadFake.answer(wanted as string, value as { ok: true; data: unknown }), [name, reply]);

// La foto de las muestras está grabando: Probar se apaga. Un estado detenido lo enciende.
const stopped = (page: Page) =>
  page.evaluate(() => {
    const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
    const state = fake.sampleState();
    fake.emit("state", { ...state, seq: state.seq + 1, obs: { ...state.obs, recording: "stopped", takeInProgress: false, streaming: false, recordedMs: 0 } }, state.seq + 1);
  });

const list = (page: Page) => page.getByRole("list", { name: "Scripts" });
const openBancocam = async (page: Page) => {
  await list(page).getByRole("button", { name: /Cámara del banco/ }).click();
  await expect(page.getByLabel("Nombre")).toHaveValue("Cámara del banco");
};

test.beforeEach(async ({ page }) => {
  await page.goto("/#/scripts");
  await expect(page.getByRole("heading", { level: 1, name: "Scripts" })).toBeVisible();
});

test("la lista con el contador, el estado y la última corrida", async ({ page }) => {
  await expect(page.getByLabel("Contador")).toHaveText("2 scripts · 1 en uso · 1 sin ficha");
  await expect(list(page).getByRole("button", { name: /Cámara del banco/ })).toContainText("Listo");
  await expect(list(page).getByRole("button", { name: /Cámara del banco/ })).toContainText("bancocam.cmd · 15 s");
  await expect(list(page).getByRole("button", { name: /suelto\.cmd/ })).toContainText("Sin ficha");
  await expect(list(page).getByRole("button", { name: /suelto\.cmd/ })).toContainText("nunca");
});

test("buscar y filtrar, en la página", async ({ page }) => {
  await expect(page.getByLabel("Contador")).toContainText("2 scripts");
  const listed = (await calls(page, "scripts.list")).length;
  await page.getByLabel("Buscar scripts").fill("ir al banco");
  await expect(list(page).getByRole("listitem")).toHaveCount(1);
  await expect(list(page)).toContainText("Cámara del banco");

  await page.getByLabel("Buscar scripts").fill("");
  await page.getByLabel("Situación").selectOption("noManifest");
  await expect(list(page).getByRole("listitem")).toHaveCount(1);
  await expect(list(page)).toContainText("suelto.cmd");

  await page.getByLabel("Situación").selectOption("all");
  await page.getByLabel("Etiqueta").selectOption("banco");
  await expect(list(page).getByRole("listitem")).toHaveCount(1);
  expect(await calls(page, "scripts.list")).toHaveLength(listed);
});

test("guardar la ficha manda la huella de lo leído y lo que la ventana no conoce", async ({ page }) => {
  await openBancocam(page);
  await page.getByLabel("Tope en segundos").fill("20");
  await page.getByRole("button", { name: "Guardar la ficha" }).click();

  await expect(page.getByText("Guardado.")).toBeVisible();
  const [sent] = (await calls(page, "scripts.write")) as Array<{ id: string; manifest: Record<string, unknown>; expectedFingerprint: string }>;
  expect(sent.id).toBe("bancocam");
  expect(sent.expectedFingerprint).toBe("3aed4eeba8413dcf2840a313b9546d8ffe0c4b324269c04fbe12b582b2caab34");
  expect(sent.manifest).toEqual({ entry: "bancocam.cmd", name: "Cámara del banco", inventado: true, timeoutSec: 20 });
});

test("con una huella vieja, ofrece recargar la ficha", async ({ page }) => {
  await openBancocam(page);
  await answer(page, "scripts.write", { ok: false, error: { code: "config_changed_on_disk", message: "script.json cambió en el disco." } });
  await page.getByLabel("Nombre").fill("Otra");
  await page.getByRole("button", { name: "Guardar la ficha" }).click();
  await page.getByRole("button", { name: "Recargar la ficha" }).click();

  await expect(page.getByLabel("Nombre")).toHaveValue("Cámara del banco");
  expect(await calls(page, "scripts.read")).toHaveLength(2);
});

test("Probar muestra las líneas de script.output en orden, marca las de error y Cancelar manda action.cancel", async ({ page }) => {
  await stopped(page);
  await openBancocam(page);
  await page.getByRole("button", { name: "Probar" }).click();
  await expect(page.getByRole("button", { name: "Cancelar la prueba" })).toBeVisible();

  await emit(page, "script.output", { runId, line: "abriendo scrcpy", stream: "stdout" }, 100);
  await emit(page, "script.output", { runId, line: "sin adb", stream: "stderr" }, 101);
  await emit(page, "script.output", { runId, line: "cámara lista", stream: "stdout" }, 102);
  const output = page.getByRole("list", { name: "Salida de la prueba" }).getByRole("listitem");
  await expect(output).toHaveText(["abriendo scrcpy", "sin adb", "cámara lista"]);
  await expect(output.nth(1)).toHaveAttribute("data-stream", "stderr");

  await page.getByRole("button", { name: "Cancelar la prueba" }).click();
  expect(await calls(page, "action.cancel")).toEqual([{ runId }]);

  await emit(page, "script.output", { runId, end: { ok: false, durationMs: 1200, message: "cancelado" } }, 103);
  await expect(page.getByText("cancelado")).toBeVisible();
  await expect(page.getByRole("button", { name: "Cancelar la prueba" })).toBeHidden();
});

test("con una toma en curso, Probar está apagado con la leyenda", async ({ page }) => {
  await openBancocam(page);

  await expect(page.getByRole("button", { name: "Probar" })).toBeDisabled();
  await expect(page.getByText("No se prueba mientras se graba.")).toBeVisible();
});

test("Quitar está apagado si el script está en uso, y dice quién lo usa", async ({ page }) => {
  await openBancocam(page);

  await expect(page.getByRole("button", { name: "Quitar" })).toBeDisabled();
  await expect(page.getByText("Lo usan: Ir al banco.")).toBeVisible();
});

// Repaso de la etapa 8, fila 9: Quitar no pide confirmación porque va al respaldo, y por eso tiene que decir adónde.
test("Quitar un script dice en qué carpeta del respaldo quedó", async ({ page }) => {
  // En las muestras no hay ninguno que se pueda quitar: bancocam está en uso y suelto.cmd es un archivo suelto.
  const viejo = { id: "viejo", name: "Viejo", entry: "viejo.cmd", wait: true, timeoutSec: 15, contract: 1, tags: [], status: "ready", problems: [], usedBy: [] };
  await answer(page, "scripts.list", { ok: true, data: { scripts: [viejo] } });
  await answer(page, "scripts.read", { ok: true, data: { script: viejo, manifest: { entry: "viejo.cmd", name: "Viejo" }, files: [], fingerprint: "f", runs: [] } });
  await page.goto("/#/settings");
  await page.goto("/#/scripts");
  await list(page).getByRole("button", { name: /Viejo/ }).click();
  await page.getByRole("button", { name: "Quitar", exact: true }).click();

  await expect(page.getByRole("status")).toHaveText("Se quitó Viejo: quedó en el respaldo, en C:\\Users\\Emmanuel\\AppData\\Roaming\\PampaPad\\backup\\scripts\\viejo-20261027-153000");
  expect(await calls(page, "scripts.delete")).toEqual([{ id: "viejo" }]);
});

test("Importar llama a scripts.import y vuelve a listar", async ({ page }) => {
  await expect(page.getByLabel("Contador")).toContainText("2 scripts");
  const listed = (await calls(page, "scripts.list")).length;
  await page.getByRole("button", { name: "Importar" }).click();

  await expect.poll(async () => (await calls(page, "scripts.list")).length).toBe(listed + 1);
  expect(await calls(page, "scripts.import")).toEqual([{}]);
});

test("el enlace de un uso lleva a su pantalla: la acción a Acciones y el atajo general a Ajustes", async ({ page }) => {
  await openBancocam(page);
  await page.getByRole("link", { name: "atajo general Ctrl+Shift+Alt+F2" }).click();
  await expect(page).toHaveURL(/#\/settings\?hotkey=0$/);

  await page.goBack();
  await page.getByRole("link", { name: "Ir al banco" }).click();
  await expect(page).toHaveURL(/#\/actions\?id=ir-al-banco$/);
  await expect(page.getByRole("heading", { level: 1, name: "Acciones" })).toBeVisible();
});

test("editar la ficha, ir a Teclas y volver deja el borrador, y window.dirty sigue en verdadero", async ({ page }) => {
  await openBancocam(page);
  await page.getByLabel("Nombre").fill("Cámara nueva");
  await expect.poll(async () => (await calls(page, "window.dirty")).at(-1)).toEqual({ dirty: true });

  await page.getByRole("navigation", { name: "Pantallas" }).getByRole("link", { name: /Teclas/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Teclas" })).toBeVisible();
  await page.getByRole("navigation", { name: "Pantallas" }).getByRole("link", { name: /Scripts/ }).click();

  await expect(page.getByLabel("Nombre")).toHaveValue("Cámara nueva");
  expect((await calls(page, "window.dirty")).at(-1)).toEqual({ dirty: true });
});

test("después de guardar, la ficha toma la fila nueva de la lista: el título no queda viejo", async ({ page }) => {
  // La lista que vuelve después de guardar trae el nombre nuevo.
  await page.evaluate(async () => {
    type Row = { id: string; name: string };
    const fake = (window as unknown as { pampapadFake: { request(name: string, data: unknown): Promise<{ data: { scripts: Row[] } }>; answer(name: string, reply: unknown): void } }).pampapadFake;
    const reply = await fake.request("scripts.list", {});
    fake.answer("scripts.list", { ok: true, data: { scripts: reply.data.scripts.map((row) => (row.id === "bancocam" ? { ...row, name: "Cámara nueva" } : row)) } });
  });
  await openBancocam(page);
  await page.getByLabel("Nombre").fill("Cámara nueva");
  await page.getByRole("button", { name: "Guardar la ficha" }).click();

  await expect(page.getByRole("heading", { level: 2, name: "Cámara nueva" })).toBeVisible();
});

test("con la ficha cambiada, abrir otro script e Importar se apagan hasta guardar o descartar", async ({ page }) => {
  await openBancocam(page);
  await page.getByLabel("Nombre").fill("Cambiado");

  await expect(page.getByText("Guardá o descartá los cambios de la ficha primero.")).toBeVisible();
  await expect(list(page).getByRole("button", { name: /suelto\.cmd/ })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Importar" })).toBeDisabled();

  await page.getByRole("button", { name: "Descartar" }).click();
  await expect(list(page).getByRole("button", { name: /suelto\.cmd/ })).toBeEnabled();
  await expect(page.getByRole("button", { name: "Importar" })).toBeEnabled();
});

test("la salida de una corrida que no lanzó esta pantalla, como una del MCP, no entra en la prueba", async ({ page }) => {
  await stopped(page);
  await openBancocam(page);
  await page.getByRole("button", { name: "Probar" }).click();
  await expect(page.getByRole("button", { name: "Cancelar la prueba" })).toBeVisible();

  const foreign = "11111111-2222-4333-8444-555555555555";
  await emit(page, "script.output", { runId: foreign, line: "de otra corrida", stream: "stdout" }, 100);
  await emit(page, "script.output", { runId: foreign, end: { ok: true, durationMs: 10, message: "ajena terminada" } }, 101);
  await emit(page, "script.output", { runId, line: "cámara lista", stream: "stdout" }, 102);

  await expect(page.getByRole("list", { name: "Salida de la prueba" }).getByRole("listitem")).toHaveText(["cámara lista"]);
  await expect(page.getByText("ajena terminada")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Cancelar la prueba" })).toBeVisible();
});
