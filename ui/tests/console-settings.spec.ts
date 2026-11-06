import { expect, test, type Page } from "@playwright/test";

// Las tarjetas de la consola en Ajustes (plan de la 9b, E9b-5): Pantalla de la consola, Cómo se cambia de página y los canales de Avisos.
type Fake = {
  calls: Array<{ name: string; data: unknown }>;
  emit(event: string, data: unknown, seq: number): void;
  request(name: string, data: unknown): Promise<{ ok: true; data: unknown }>;
  sampleState(): Record<string, unknown> & { seq: number; console: Record<string, unknown> };
};

type Saved = { config: Record<string, unknown> };
const lastSaved = (page: Page) =>
  page.evaluate(() => ((window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === "config.save").at(-1)?.data as Saved | undefined)?.config);

const withConfig = (page: Page, patch: Record<string, unknown>) =>
  page.evaluate(async (more) => {
    const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
    const reply = (await fake.request("app.snapshot", {})) as { ok: true; data: { config: Record<string, unknown>; fingerprint: string } };
    fake.emit("config.changed", { config: { ...reply.data.config, ...more }, fingerprint: reply.data.fingerprint }, 500);
  }, patch);

// La consola de las muestras saluda con 0xC7 (la 0.1.5); console cambia lo que dice el Hello.
const withConsole = (page: Page, console: Record<string, unknown>) =>
  page.evaluate((more) => {
    const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
    const state = fake.sampleState();
    fake.emit("state", { ...state, seq: state.seq + 10, console: { ...state.console, ...more } }, state.seq + 10);
  }, console);

const card = (page: Page, name: string) => page.getByRole("region", { name });
const screenCard = (page: Page) => card(page, "Pantalla de la consola");
const save = (page: Page) => page.getByRole("button", { name: "Guardar", exact: true }).click();

test.beforeEach(async ({ page }) => {
  await page.goto("/#/settings");
  await expect(page.getByRole("heading", { level: 1, name: "Ajustes" })).toBeVisible();
});

test("la tarjeta dice el saludo de la consola, y sin consola que los ajustes se mandan cuando se conecte", async ({ page }) => {
  await expect(screenCard(page).getByText("Consola conectada · firmware 0.1.5 · ajustes al día")).toBeVisible();
  await expect(screenCard(page).getByText("Siempre encendida mientras se graba o se está en pausa.")).toBeVisible();

  await withConsole(page, { state: "waiting" });
  await expect(screenCard(page).getByText("Sin consola: se mandan cuando se conecte")).toBeVisible();
});

test("mover el brillo y guardar manda config.save con el valor; volverlo al de fábrica no escribe nada", async ({ page }) => {
  const brightness = screenCard(page).getByLabel("Brillo", { exact: true });
  await expect(brightness).toHaveValue("80");

  await brightness.fill("40");
  await expect(screenCard(page).getByText("Brillo: 40 %")).toBeVisible();
  await save(page);
  expect((await lastSaved(page))?.console).toEqual({ screen: { brightness: 40 } });

  await brightness.fill("80");
  await save(page);
  expect(await lastSaved(page)).not.toHaveProperty("console");
});

test("Nunca escribe 0 en el tiempo de atenuar", async ({ page }) => {
  await screenCard(page).getByLabel("Atenuar tras").selectOption({ label: "Nunca" });
  await save(page);

  expect((await lastSaved(page))?.console).toEqual({ screen: { dimAfterSec: 0 } });
});

test("Dormir del todo se apaga si el Hello no declara el bit 3, como la 0.1.5 con 0xC7, y se prende si lo declara", async ({ page }) => {
  const sleep = screenCard(page).getByLabel("Dormir del todo tras");
  await expect(sleep).toBeDisabled();
  await expect(screenCard(page).getByText("Esta consola no duerme")).toBeVisible();

  await withConsole(page, { capabilities: 0xcf });
  await expect(sleep).toBeEnabled();
  await sleep.selectOption({ label: "30 min" });
  await save(page);
  expect((await lastSaved(page))?.console).toEqual({ sleepAfterSec: 1800 });
});

test("la página de la tecla del módulo se elige y se quita, y Sin asignar no deja moduleKey", async ({ page }) => {
  await withConfig(page, { pages: [{ id: "grabacion", name: "Grabación", keys: [] }, { id: "escenas", name: "Escenas", keys: [] }] });
  const layer = card(page, "Cómo se cambia de página").getByLabel("Página que se ve mientras se mantiene la tecla del módulo");
  await expect(layer.locator("option:checked")).toHaveText("Sin asignar");

  await layer.selectOption({ label: "Escenas" });
  await save(page);
  expect((await lastSaved(page))?.moduleKey).toEqual({ layer: "escenas" });

  await layer.selectOption({ label: "Sin asignar" });
  await save(page);
  expect(await lastSaved(page)).not.toHaveProperty("moduleKey");
});

test("apagar el sonido de un aviso lo escribe en console.alerts, y no_pc viene con luz y sonido (0.2.1)", async ({ page }) => {
  const alerts = card(page, "Avisos");
  await expect(alerts.getByRole("switch", { name: "Sonido: Sin PC" })).toHaveAttribute("aria-checked", "true");
  await expect(alerts.getByRole("switch", { name: "Luz: Sin PC" })).toHaveAttribute("aria-checked", "true");

  await alerts.getByRole("switch", { name: "Sonido: Micrófono silenciado grabando" }).click();
  await save(page);

  expect((await lastSaved(page))?.console).toEqual({ alerts: { mic_muted: { sound: false } } });
});

test("Volver a los valores por defecto pide confirmación y borra sólo el bloque console", async ({ page }) => {
  await withConfig(page, { moduleKey: { layer: "grabacion" }, console: { holdMs: 2000, alerts: { obs_lost: { light: false } }, inventado: 1 } });
  await expect(screenCard(page).getByText("Mantener para PARAR y PAUSAR: 2,0 s")).toBeVisible();

  await screenCard(page).getByRole("button", { name: "Volver a los valores por defecto" }).click();
  await screenCard(page).getByRole("button", { name: "No" }).click();
  await expect(page.getByText("Cambios sin guardar en: Biblioteca")).toHaveCount(0);

  await screenCard(page).getByRole("button", { name: "Volver a los valores por defecto" }).click();
  await screenCard(page).getByRole("button", { name: "Sí, volver" }).click();
  await expect(screenCard(page).getByText("Mantener para PARAR y PAUSAR: 1,0 s")).toBeVisible();
  await save(page);

  const saved = await lastSaved(page);
  expect(saved).not.toHaveProperty("console");
  expect(saved?.moduleKey).toEqual({ layer: "grabacion" });
  expect(saved?.pages).toBeDefined();
  await expect(screenCard(page).getByRole("button", { name: "Volver a los valores por defecto" })).toBeDisabled();
});

test("el doble toque de la tecla del módulo se elige y se quita sin tocar la capa, y una acción que no existe se ve", async ({ page }) => {
  await withConfig(page, { moduleKey: { layer: "grabacion", doubleTap: "fantasma" } });
  const doubleTap = card(page, "Cómo se cambia de página").getByLabel("Doble toque de la tecla del módulo");
  await expect(doubleTap.locator("option:checked")).toHaveText("fantasma (no existe)");
  await expect(card(page, "Cómo se cambia de página").getByText("el toque solo espera un instante")).toBeVisible();

  await doubleTap.selectOption({ label: "Marca: short" });
  await save(page);
  expect((await lastSaved(page))?.moduleKey).toEqual({ layer: "grabacion", doubleTap: "marca-short" });

  await doubleTap.selectOption({ label: "Sin asignar" });
  await save(page);
  expect((await lastSaved(page))?.moduleKey).toEqual({ layer: "grabacion" });
});
