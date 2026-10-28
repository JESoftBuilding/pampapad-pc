import { expect, test, type Page } from "@playwright/test";

type Fake = {
  calls: Array<{ name: string; data: unknown }>;
  emit(event: string, data: unknown, seq: number): void;
  answer(name: string, reply: { ok: true; data: unknown } | { ok: false; error: { code: string; message: string } }): void;
  sampleState(): Record<string, unknown> & { seq: number; obs: Record<string, unknown> };
};

// La foto de las muestras está grabando (Contract.SampleState): Probar se apaga. Un estado detenido lo enciende.
const stopped = (page: Page) =>
  page.evaluate(() => {
    const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
    const state = fake.sampleState();
    fake.emit("state", { ...state, seq: state.seq + 1, obs: { ...state.obs, recording: "stopped", takeInProgress: false, streaming: false, recordedMs: 0 } }, state.seq + 1);
  });

type SaveCall = { config: { actions: Array<Record<string, unknown>> } };

const emit = (page: Page, event: string, data: unknown, seq: number) =>
  page.evaluate(([name, payload, number]) => (window as unknown as { pampapadFake: Fake }).pampapadFake.emit(name as string, payload, number as number), [event, data, seq]);
const calls = (page: Page, name: string) => page.evaluate((wanted) => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === wanted).map((call) => call.data), name);
const answerOptions = (page: Page, items: unknown[]) => page.evaluate((list) => (window as unknown as { pampapadFake: Fake }).pampapadFake.answer("options.list", { ok: true, data: { items: list } }), items);

test.beforeEach(async ({ page }) => {
  await page.goto("/#/actions");
  await expect(page.getByRole("heading", { level: 1, name: "Acciones" })).toBeVisible();
});

test("abre con la lista de acciones, cuántos pasos tiene cada una y dónde se usa", async ({ page }) => {
  const list = page.getByRole("list", { name: "Acciones" });

  await expect(list.getByRole("button", { name: /Marca: short/ })).toContainText("1 paso");
  await expect(list.getByRole("button", { name: /Marca: short/ })).toContainText("Grabación, tecla 5");
  await expect(list.getByRole("button", { name: /Marca: inicio/ })).toContainText("Grabación, tecla 6");
});

test("armar ir al banco desde el menú de pasos, con su sólo si, y guardar", async ({ page }) => {
  await page.getByLabel("Nombre de la acción nueva").fill("Ir al banco");
  await page.getByRole("button", { name: "Crear acción" }).click();
  await expect(page.getByLabel("Nombre", { exact: true })).toHaveValue("Ir al banco");
  await expect(page.getByText("id: ir-al-banco")).toBeVisible();

  await answerOptions(page, [{ name: "bancocam", detail: "Cámara del banco" }]);
  await page.getByLabel("Tipo de paso").selectOption("script.run");
  await page.getByRole("button", { name: "Agregar paso" }).click();
  await page.getByLabel("Script", { exact: true }).selectOption("bancocam");
  await expect(page.getByLabel("Esperar a que termine").locator("option:checked")).toHaveText("Como dice la ficha");
  await page.getByLabel("Sólo si").selectOption("window.exists");
  await page.getByLabel("Título de la ventana").fill("bancocam");
  await page.getByRole("switch", { name: "Al revés (negate)" }).click();

  await page.getByLabel("Tipo de paso").selectOption("wait.window");
  await page.getByRole("button", { name: "Agregar paso" }).click();
  await page.getByLabel("Título de la ventana").fill("bancocam");
  await page.getByLabel("Cuánto esperar, en segundos").fill("5");

  await answerOptions(page, [{ name: "Cámara", uuid: "uuid-camara" }, { name: "Banco", uuid: "uuid-banco" }]);
  await page.getByLabel("Tipo de paso").selectOption("obs.scene");
  await page.getByRole("button", { name: "Agregar paso" }).click();
  await page.getByLabel("Escena", { exact: true }).selectOption({ label: "Banco" });
  await page.getByRole("button", { name: "Guardar" }).click();

  await expect(page.getByText("Guardado.")).toBeVisible();
  const saves = (await calls(page, "config.save")) as SaveCall[];
  expect(saves[0].config.actions.at(-1)).toEqual({
    id: "ir-al-banco",
    name: "Ir al banco",
    steps: [
      { type: "script.run", script: "bancocam", onlyIf: { type: "window.exists", title: "bancocam", negate: true } },
      { type: "wait.window", title: "bancocam", timeoutSec: 5 },
      { type: "obs.scene", scene: { uuid: "uuid-banco", name: "Banco" } },
    ],
  });
});

test("un booleano opcional tiene tres valores y Como dice la ficha saca la clave del borrador", async ({ page }) => {
  await page.getByLabel("Nombre de la acción nueva").fill("Con script");
  await page.getByRole("button", { name: "Crear acción" }).click();
  await answerOptions(page, [{ name: "bancocam", detail: "Cámara del banco" }]);
  await page.getByLabel("Tipo de paso").selectOption("script.run");
  await page.getByRole("button", { name: "Agregar paso" }).click();
  await page.getByLabel("Script", { exact: true }).selectOption("bancocam");

  const wait = page.getByLabel("Esperar a que termine");
  await wait.selectOption({ label: "No" });
  await page.getByRole("button", { name: "Guardar" }).click();
  await expect(page.getByText("Guardado.")).toBeVisible();
  await wait.selectOption({ label: "Como dice la ficha" });
  await page.getByRole("button", { name: "Guardar" }).click();

  await expect.poll(async () => (await calls(page, "config.save")).length).toBe(2);
  const saves = (await calls(page, "config.save")) as SaveCall[];
  expect(saves[0].config.actions.at(-1)?.steps).toEqual([{ type: "script.run", script: "bancocam", wait: false }]);
  expect(saves[1].config.actions.at(-1)?.steps).toEqual([{ type: "script.run", script: "bancocam" }]);
  expect(Object.keys((saves[1].config.actions.at(-1)?.steps as Array<Record<string, unknown>>)[0])).not.toContain("wait");
});

test("subir y bajar mueven el paso con su condición, y quitar lo saca", async ({ page }) => {
  await page.getByLabel("Nombre de la acción nueva").fill("Dos pasos");
  await page.getByRole("button", { name: "Crear acción" }).click();
  await page.getByLabel("Tipo de paso").selectOption("wait.time");
  await page.getByRole("button", { name: "Agregar paso" }).click();
  await page.getByLabel("Cuánto, en milisegundos").fill("500");
  await page.getByLabel("Sólo si").selectOption("recording.is");
  await page.getByLabel("Estado", { exact: true }).selectOption("stopped");
  await page.getByLabel("Tipo de paso").selectOption("obs.marker");
  await page.getByRole("button", { name: "Agregar paso" }).click();
  await page.getByLabel("Tipo de marca").selectOption("short");

  await page.getByRole("button", { name: "Subir el paso 2" }).click();
  await page.getByRole("button", { name: "Guardar" }).click();

  const saves = (await calls(page, "config.save")) as SaveCall[];
  expect(saves[0].config.actions.at(-1)).toEqual({
    id: "dos-pasos",
    name: "Dos pasos",
    steps: [
      { type: "obs.marker", marker: "short" },
      { type: "wait.time", durationMs: 500, onlyIf: { type: "recording.is", state: "stopped" } },
    ],
  });

  await page.getByRole("button", { name: "Quitar el paso 1" }).click();
  await expect(page.getByRole("listitem").filter({ hasText: "Esperar un rato" })).toHaveCount(1);
  await expect(page.getByRole("listitem").filter({ hasText: "Marcar capítulo" })).toHaveCount(0);
});

test("Probar manda el borrador, muestra el resultado por paso y se cancela por su runId", async ({ page }) => {
  await stopped(page);
  await page.getByRole("list", { name: "Acciones" }).getByRole("button", { name: /Marca: short/ }).click();
  await page.getByRole("button", { name: "Probar" }).click();

  const tests = (await calls(page, "action.test")) as Array<{ action: { id: string } }>;
  expect(tests.map((call) => call.action.id)).toEqual(["marca-short"]);
  await page.getByRole("button", { name: "Cancelar la prueba" }).click();
  expect(await calls(page, "action.cancel")).toEqual([{ runId: "0f8fad5b-d9cb-469f-a165-70867728950e" }]);

  await emit(
    page,
    "action.finished",
    { actionId: "marca-short", runId: "0f8fad5b-d9cb-469f-a165-70867728950e", requestedBy: "window", ok: false, stepNumber: 1, stepType: "obs.marker", failure: { code: "cancelled", message: "Se canceló." }, durationMs: 40, steps: [{ outcome: "failed", message: "Se canceló." }] },
    43,
  );
  await expect(page.getByRole("list", { name: "Resultado de la prueba" })).toContainText("falló");
  await expect(page.getByRole("list", { name: "Resultado de la prueba" })).toContainText("Se canceló.");
});

test("una acción en uso no se quita y dice dónde se usa", async ({ page }) => {
  await page.getByRole("list", { name: "Acciones" }).getByRole("button", { name: /Marca: short/ }).click();

  const remove = page.getByRole("button", { name: "Quitar la acción" });
  await expect(remove).toBeDisabled();
  await expect(page.getByText("La usan: Grabación, tecla 5")).toBeVisible();
});

test("un paso de un tipo desconocido se conserva tal cual, se ve roto y se guarda igual", async ({ page }) => {
  await emit(
    page,
    "config.changed",
    {
      config: {
        version: 1,
        pages: [],
        actions: [{ id: "luces", name: "Luces", steps: [{ type: "lights", command: "power", value: "toggle", inventado: "se queda" }] }],
      },
      fingerprint: "otra",
    },
    43,
  );
  await page.getByRole("list", { name: "Acciones" }).getByRole("button", { name: /Luces/ }).click();
  await page.getByRole("button", { name: /lights \(tipo desconocido\)/ }).click();

  await expect(page.getByText("Este programa no conoce el tipo lights")).toBeVisible();
  await expect(page.getByRole("textbox", { name: "El paso, tal cual está en config.json" })).toHaveValue(/"inventado": "se queda"/);
  await page.getByLabel("Nombre", { exact: true }).fill("Luces de la sala");
  await page.getByRole("button", { name: "Guardar" }).click();

  const saves = (await calls(page, "config.save")) as Array<SaveCall & { expectedFingerprint: string }>;
  expect(saves[0].expectedFingerprint).toBe("otra");
  expect(saves[0].config.actions[0]).toEqual({ id: "luces", name: "Luces de la sala", steps: [{ type: "lights", command: "power", value: "toggle", inventado: "se queda" }] });
});

test("sin OBS, la escena se escribe a mano y el campo dice por qué", async ({ page }) => {
  await page.evaluate(() =>
    (window as unknown as { pampapadFake: Fake }).pampapadFake.answer("options.list", {
      ok: true,
      data: { items: [], unavailable: "OBS no está conectado (closed): el campo acepta el nombre escrito a mano." },
    }),
  );
  await page.getByLabel("Nombre de la acción nueva").fill("Pantalla");
  await page.getByRole("button", { name: "Crear acción" }).click();
  await page.getByLabel("Tipo de paso").selectOption("obs.scene");
  await page.getByRole("button", { name: "Agregar paso" }).click();

  await expect(page.getByText("OBS no está conectado")).toBeVisible();
  await page.getByLabel("Escena", { exact: true }).fill("Pantalla");
  await page.getByRole("button", { name: "Guardar" }).click();

  const saves = (await calls(page, "config.save")) as SaveCall[];
  expect(saves[0].config.actions.at(-1)).toEqual({ id: "pantalla", name: "Pantalla", steps: [{ type: "obs.scene", scene: "Pantalla" }] });
});

test("un action.finished de una corrida que no lanzó esta pantalla, como una del MCP, no cierra la prueba", async ({ page }) => {
  await stopped(page);
  await page.getByRole("list", { name: "Acciones" }).getByRole("button", { name: /Marca: short/ }).click();
  await page.getByRole("button", { name: "Probar" }).click();

  await emit(
    page,
    "action.finished",
    { actionId: "marca-short", runId: "11111111-2222-4333-8444-555555555555", requestedBy: "mcp", ok: false, stepNumber: 1, stepType: "obs.marker", failure: { code: "cancelled", message: "Ajena." }, durationMs: 40, steps: [{ outcome: "failed", message: "Ajena." }] },
    43,
  );

  await expect(page.getByRole("button", { name: "Cancelar la prueba" })).toBeVisible();
  await expect(page.getByText("Ajena.")).toHaveCount(0);
});
