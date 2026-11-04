import { expect, test, type Page } from "@playwright/test";

type Reply = { ok: true; data: unknown } | { ok: false; error: { code: string; message: string } };
type Fake = {
  calls: Array<{ name: string; data: unknown }>;
  emit(event: string, data: unknown, seq: number): void;
  answer(name: string, reply: Reply): void;
  sampleState(): Record<string, unknown> & { seq: number; console: Record<string, unknown> };
};

const calls = (page: Page, name: string) => page.evaluate((wanted) => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === wanted).map((call) => call.data), name);
const answer = (page: Page, name: string, reply: Reply) => page.evaluate(([wanted, value]) => (window as unknown as { pampapadFake: Fake }).pampapadFake.answer(wanted as string, value as Reply), [name, reply] as const);
const consoleCard = (page: Page) => page.getByRole("region", { name: "Conexión con la consola" });
const portList = (page: Page) => consoleCard(page).getByLabel("Puerto de la consola");

// La tarjeta pide console.ports al aparecer: lo que conteste el puente se fija antes de entrar a Ajustes.
const openSettings = async (page: Page, ports?: unknown[]) => {
  await page.goto("/#/actions");
  await expect(page.getByRole("heading", { level: 1, name: "Acciones" })).toBeVisible();
  if (ports !== undefined) {
    await answer(page, "console.ports", { ok: true, data: { ports } });
  }

  await page.evaluate(() => (window.location.hash = "#/settings"));
  await expect(page.getByRole("heading", { level: 1, name: "Ajustes" })).toBeVisible();
};

test("muestra el puerto de ahora con su estado, la lista con la descripción de Windows y cuándo se aplica", async ({ page }) => {
  await openSettings(page);

  await expect(consoleCard(page).getByText("COM20 · Consola conectada · firmware 0.1.5 · ajustes al día")).toBeVisible();
  await expect(consoleCard(page).getByText("todavía no saluda", { exact: false })).toHaveCount(0);
  await expect(portList(page).locator("option:checked")).toHaveText("COM20 · USB-Enhanced-SERIAL CH343");
  await expect(portList(page).locator("option")).toHaveText(["Sin consola", "COM1 · Puerto de comunicaciones", "COM20 · USB-Enhanced-SERIAL CH343"]);
  await expect(consoleCard(page).getByText("Se aplica al volver a abrir PampaPad")).toBeVisible();
  await expect(consoleCard(page).getByRole("button", { name: /Automático|Soltar/ })).toHaveCount(0);
});

test("elegir un puerto y guardar manda settings.save con consolePort", async ({ page }) => {
  await openSettings(page, [
    { port: "COM8", description: "USB-Enhanced-SERIAL CH9102", usbId: "1A86:55D4" },
    { port: "COM20", description: "USB-Enhanced-SERIAL CH343", usbId: "1A86:55D3" },
  ]);
  await answer(page, "settings.save", { ok: true, data: { hotkeysEnabled: true, startWithWindows: "on", mcpEnabled: true, consolePort: "COM8" } });

  await portList(page).selectOption("COM8");
  await expect(page.getByText("Cambios sin guardar en: Esta PC")).toBeVisible();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  expect(await calls(page, "settings.save")).toEqual([{ consolePort: "COM8" }]);
  await expect(portList(page).locator("option:checked")).toHaveText("COM8 · USB-Enhanced-SERIAL CH9102");
});

test("Sin consola manda consolePort vacío", async ({ page }) => {
  await openSettings(page);
  await answer(page, "settings.save", { ok: true, data: { hotkeysEnabled: true, startWithWindows: "on", mcpEnabled: true } });

  await portList(page).selectOption({ label: "Sin consola" });
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  expect(await calls(page, "settings.save")).toEqual([{ consolePort: "" }]);
  await expect(portList(page).locator("option:checked")).toHaveText("Sin consola");
});

test("sin puertos serie lo dice, y el puerto guardado que no está se sigue viendo", async ({ page }) => {
  await openSettings(page, []);

  await expect(consoleCard(page).getByText("No hay puertos serie")).toBeVisible();
  await expect(portList(page).locator("option:checked")).toHaveText("COM20 (no está)");
});
