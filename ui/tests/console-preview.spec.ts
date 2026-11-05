import { expect, test, type Page } from "@playwright/test";
import samples from "../src/bridge/contract.samples.json" with { type: "json" };

// La vista previa de Teclas (plan de la 9b, E9b-6): se pide con el borrador, no con lo guardado, y con el estado de muestra elegido.
type Fake = { calls: Array<{ name: string; data: unknown }>; answer(name: string, reply: { ok: true; data: unknown }): void };
type PreviewCall = { config: { pages: Array<{ id: string; keys: Array<{ position: number; shortLabel?: string[] }> }> }; state: string; page?: string };

const previews = (page: Page) =>
  page.evaluate(() => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === "console.preview").map((call) => call.data)) as Promise<PreviewCall[]>;

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
