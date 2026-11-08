import { expect, test, type Page } from "@playwright/test";

// Las luces de la PC (plan de la 1.6.0, RGB-4): la tarjeta de Ajustes y el renglón de Estado, con el puente falso.
type Reply = { ok: true; data: unknown } | { ok: false; error: { code: string; message: string } };
type Fake = {
  calls: Array<{ name: string; data: unknown }>;
  emit(event: string, data: unknown, seq: number): void;
  answer(name: string, reply: Reply): void;
  sampleState(): Record<string, unknown> & { seq: number };
};

const board = "ASUS ROG STRIX B450-F GAMING II";
const zones = [
  { name: "Fans", detail: "Corsair" },
  { name: "Back I/O", detail: board },
  { name: "RGB Header", detail: board },
];
const saved = { hotkeysEnabled: true, startWithWindows: "on", mcpEnabled: true };

const calls = (page: Page, name: string) => page.evaluate((wanted) => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === wanted).map((call) => call.data), name);
const answer = (page: Page, name: string, reply: Reply) => page.evaluate(([wanted, value]) => (window as unknown as { pampapadFake: Fake }).pampapadFake.answer(wanted as string, value as Reply), [name, reply] as const);
const card = (page: Page) => page.getByRole("region", { name: "Luces de la PC" });
const zoneList = (page: Page) => card(page).getByLabel("Zona");
const emitPcLights = (page: Page, pcLights: Record<string, unknown>, step: number) =>
  page.evaluate(
    ([value, after]) => {
      const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
      const state = fake.sampleState();
      fake.emit("state", { ...state, seq: state.seq + after, pcLights: value }, state.seq + after);
    },
    [pcLights, step] as const,
  );

// La tarjeta pide options.list al aparecer: lo que conteste el puente se fija antes de entrar a Ajustes. La muestra ya trae elegida
// "ASUS ROG STRIX B450-F GAMING II · RGB Header".
const openSettings = async (page: Page, reply: Reply) => {
  await page.goto("/#/actions");
  await expect(page.getByRole("heading", { level: 1, name: "Acciones" })).toBeVisible();
  await answer(page, "options.list", reply);

  await page.evaluate(() => (window.location.hash = "#/settings"));
  await expect(page.getByRole("heading", { level: 1, name: "Ajustes" })).toBeVisible();
};

test("lista No manejar y las zonas como dispositivo · zona, con la elegida marcada y el estado arriba", async ({ page }) => {
  await openSettings(page, { ok: true, data: { items: zones } });

  await expect(zoneList(page).locator("option")).toHaveText(["No manejar", "Corsair · Fans", `${board} · Back I/O`, `${board} · RGB Header`]);
  await expect(zoneList(page).locator("option:checked")).toHaveText(`${board} · RGB Header`);
  await expect(card(page).getByText(`Conectadas a OpenRGB · ${board} · RGB Header`)).toBeVisible();
  await expect(card(page).getByText("Sólo esta PC")).toBeVisible();
});

test("elegir una zona y guardar manda settings.save con pcLights", async ({ page }) => {
  await openSettings(page, { ok: true, data: { items: zones } });
  await answer(page, "settings.save", { ok: true, data: { ...saved, pcLights: { device: "Corsair", zone: "Fans" } } });

  await zoneList(page).selectOption({ label: "Corsair · Fans" });
  await expect(page.getByText("Cambios sin guardar en: Esta PC")).toBeVisible();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  expect(await calls(page, "settings.save")).toEqual([{ pcLights: { device: "Corsair", zone: "Fans" } }]);
  await expect(zoneList(page).locator("option:checked")).toHaveText("Corsair · Fans");
});

test("No manejar manda device y zone vacíos y queda elegido", async ({ page }) => {
  await openSettings(page, { ok: true, data: { items: zones } });
  await answer(page, "settings.save", { ok: true, data: saved });

  await zoneList(page).selectOption({ label: "No manejar" });
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  expect(await calls(page, "settings.save")).toEqual([{ pcLights: { device: "", zone: "" } }]);
  await expect(zoneList(page).locator("option:checked")).toHaveText("No manejar");
});

test("sin OpenRGB lo dice y conserva la zona elegida aunque no esté en la lista", async ({ page }) => {
  await openSettings(page, { ok: true, data: { items: [], unavailable: "OpenRGB no responde: ¿está corriendo con su servidor?" } });

  await expect(card(page).getByText("OpenRGB no responde: ¿está corriendo con su servidor? Se conserva la zona elegida.")).toBeVisible();
  await expect(zoneList(page).locator("option:checked")).toHaveText(`${board} · RGB Header (no está)`);
  expect(await calls(page, "settings.save")).toEqual([]);
});

test("una zona guardada que OpenRGB ya no da se sigue viendo, marcada como que no está", async ({ page }) => {
  await openSettings(page, { ok: true, data: { items: zones.slice(0, 2) } });

  await expect(zoneList(page).locator("option")).toHaveText(["No manejar", `${board} · RGB Header (no está)`, "Corsair · Fans", `${board} · Back I/O`]);
  await expect(zoneList(page).locator("option:checked")).toHaveText(`${board} · RGB Header (no está)`);
});

test("el renglón de Estado dice sin zona, conectadas o sin respuesta, con la zona", async ({ page }) => {
  await page.goto("/#/status");
  const row = page.getByRole("definition").filter({ hasText: /OpenRGB|Sin manejar/ });

  await expect(row).toHaveText(`Conectadas a OpenRGB · ${board} · RGB Header`);
  await emitPcLights(page, { state: "unreachable", zone: `${board} · RGB Header` }, 1);
  await expect(row).toHaveText(`OpenRGB no responde · ${board} · RGB Header · reintenta cada 5 s`);
  await emitPcLights(page, { state: "off" }, 2);
  await expect(row).toHaveText("Sin manejar: no hay una zona elegida en Ajustes");
});
