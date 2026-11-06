import { expect, test, type Page } from "@playwright/test";
import samples from "../src/bridge/contract.samples.json" with { type: "json" };

// La vista previa de Teclas (plan de la 9b, E9b-6): se pide con el borrador, no con lo guardado, y con el estado de muestra elegido.
type Fake = { calls: Array<{ name: string; data: unknown }>; answer(name: string, reply: { ok: true; data: unknown }): void };
type PreviewCall = { config: { pages: Array<{ id: string; keys: Array<{ position: number; shortLabel?: string[] }> }> }; state: string; page?: string };

const previews = (page: Page) =>
  page.evaluate(() => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === "console.preview").map((call) => call.data)) as Promise<PreviewCall[]>;

const lastSave = (page: Page) =>
  page.evaluate(() => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === "config.save").at(-1)?.data as { config: Record<string, unknown> } | undefined);

const withConfig = (page: Page, patch: Record<string, unknown>) =>
  page.evaluate(async (more) => {
    type Emitter = { request(name: string, data: unknown): Promise<{ data: { config: object; fingerprint: string } }>; emit(event: string, data: unknown, seq: number): void };
    const fake = (window as unknown as { pampapadFake: Emitter }).pampapadFake;
    const reply = await fake.request("app.snapshot", {});
    fake.emit("config.changed", { config: { ...reply.data.config, ...more }, fingerprint: reply.data.fingerprint }, 500);
  }, patch);

const pixels = (page: Page) => page.getByRole("img", { name: "La pantalla de la consola en su resolución real" }).evaluate((canvas) => (canvas as HTMLCanvasElement).toDataURL());

test.beforeEach(async ({ page }) => {
  await page.goto("/#/keys");
  await expect(page.getByRole("heading", { level: 1, name: "Teclas" })).toBeVisible();
  await expect.poll(async () => (await previews(page)).length).toBeGreaterThan(0);
});

test("cambiar la etiqueta corta pide la vista previa con el borrador nuevo y la pantalla cambia", async ({ page }) => {
  const before = await pixels(page);
  const first = (await previews(page))[0];
  expect(first).toMatchObject({ state: "recording", page: "grabacion" });

  // El puente falso contesta siempre la muestra: a partir de acá, la tecla 5 con lo que diría el C# del borrador nuevo.
  const frame = structuredClone(samples.requests["console.preview"].response.data);
  frame.keys[4].line1 = "NUEVA";
  await page.evaluate((data) => (window as unknown as { pampapadFake: Fake }).pampapadFake.answer("console.preview", { ok: true, data }), frame);
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByLabel("Línea 1", { exact: true }).fill("Nueva");

  await expect.poll(async () => (await previews(page)).at(-1)?.config.pages[0].keys.find((key) => key.position === 5)?.shortLabel).toEqual(["NUEVA"]);
  await expect.poll(() => pixels(page)).not.toBe(before);
});

test("el selector de estado pide la vista previa en el estado elegido, y ×2 la agranda", async ({ page }) => {
  await page.getByRole("group", { name: "Estado de la grabación en la vista previa" }).getByRole("button", { name: "En pausa" }).click();
  await expect.poll(async () => (await previews(page)).at(-1)?.state).toBe("paused");
  await page.getByRole("button", { name: "Sin OBS" }).click();
  await expect.poll(async () => (await previews(page)).at(-1)?.state).toBe("noObs");
  await expect(page.getByRole("button", { name: "Sin OBS" })).toHaveAttribute("aria-pressed", "true");

  const canvas = page.getByRole("img", { name: "La pantalla de la consola en su resolución real" });
  await expect(canvas).toHaveCSS("width", "320px");
  await page.getByRole("button", { name: "×2" }).click();
  await expect(canvas).toHaveCSS("width", "640px");
});

test("la perilla y la tecla del módulo dicen qué hacen, y elegir una tecla vuelve al editor", async ({ page }) => {
  await page.getByRole("button", { name: "Perilla" }).click();
  await expect(page.getByText("Atiende el aviso de la consola")).toBeVisible();
  await page.getByRole("button", { name: "Tecla del módulo" }).click();
  await expect(page.getByText("Mantener y apretar 1 a 4")).toBeVisible();
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await expect(page.getByLabel("Línea 1", { exact: true })).toBeVisible();
});

test("una respuesta vieja de console.preview que llega después de una nueva no la pisa", async ({ page }) => {
  // Se retienen los pedidos de la vista previa para contestarlos en el orden que se quiera.
  await page.evaluate(() => {
    const fake = window as unknown as { pampapadFake: { request: (name: string, data: unknown) => Promise<unknown> } };
    const held: Array<(reply: unknown) => void> = [];
    (window as unknown as { held: typeof held }).held = held;
    const original = fake.pampapadFake.request.bind(fake.pampapadFake);
    fake.pampapadFake.request = (name, data) => (name === "console.preview" ? new Promise((resolve) => held.push(resolve)) : original(name, data));
  });
  const reply = (index: number, line1: string) =>
    page.evaluate(
      ([i, text, base]) => {
        const data = structuredClone(base as { keys: Array<{ line1: string }> });
        data.keys[4].line1 = text as string;
        (window as unknown as { held: Array<(reply: unknown) => void> }).held[i as number]({ ok: true, data });
      },
      [index, line1, samples.requests["console.preview"].response.data] as const,
    );
  const held = () => page.evaluate(() => (window as unknown as { held: unknown[] }).held.length);

  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByLabel("Línea 1", { exact: true }).fill("Vieja");
  await expect.poll(held).toBe(1);
  await page.getByLabel("Línea 1", { exact: true }).fill("Nueva");
  await expect.poll(held).toBe(2);

  await reply(1, "NUEVA");
  const fresh = await pixels(page);
  await expect.poll(() => pixels(page)).toBe(fresh);
  await reply(0, "VIEJA");
  await page.waitForTimeout(300);
  expect(await pixels(page)).toBe(fresh);
});

test("con la perilla elegida en el dibujo, Probar queda deshabilitado", async ({ page }) => {
  // Las muestras están grabando y Probar se apaga por eso: se detiene para que lo único que lo apague sea la perilla.
  await page.evaluate(() => {
    type Emitter = { sampleState(): { seq: number; obs: object }; emit(event: string, data: unknown, seq: number): void };
    const fake = (window as unknown as { pampapadFake: Emitter }).pampapadFake;
    const state = fake.sampleState();
    fake.emit("state", { ...state, seq: state.seq + 1, obs: { ...state.obs, recording: "stopped", takeInProgress: false, streaming: false, recordedMs: 0 } }, state.seq + 1);
  });
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await expect(page.getByRole("button", { name: "Probar", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Perilla" }).click();
  await expect(page.getByRole("button", { name: "Probar", exact: true })).toBeDisabled();
});

test("la perilla se elige por página: Brillo de las luces escribe knob sólo en esa, y un valor desconocido se ve tal cual", async ({ page }) => {
  await withConfig(page, { pages: [{ id: "grabacion", name: "Grabación", keys: [], knob: "volumen" }, { id: "luces", name: "Luces", keys: [] }] });
  await page.getByRole("button", { name: "Perilla" }).click();
  const knob = page.getByLabel("En esta página, la perilla");
  await expect(knob.locator("option:checked")).toHaveText("volumen (no la conoce este programa)");

  await page.getByRole("button", { name: "Luces" }).click();
  await expect(knob.locator("option:checked")).toHaveText("Cambia de página");
  await knob.selectOption({ label: "Brillo de las luces" });
  await page.getByRole("button", { name: "Guardar", exact: true }).click();
  await expect(page.getByText("Guardado.")).toBeVisible();
  expect((await lastSave(page))?.config.pages).toEqual([{ id: "grabacion", name: "Grabación", keys: [], knob: "volumen" }, { id: "luces", name: "Luces", keys: [], knob: "brightness" }]);
});

test("la tecla del módulo dice la acción del doble toque y que se cambia en Ajustes", async ({ page }) => {
  await page.getByRole("button", { name: "Tecla del módulo" }).click();
  await expect(page.getByText("Sin asignar")).toBeVisible();
  await withConfig(page, { moduleKey: { doubleTap: "marca-short" } });
  await expect(page.getByText("Marca: short")).toBeVisible();
  await expect(page.getByText("se eligen en Ajustes")).toBeVisible();
});
