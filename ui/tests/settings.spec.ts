import { expect, test, type Page } from "@playwright/test";

type Reply = { ok: true; data: unknown } | { ok: false; error: { code: string; message: string } };
type Fake = {
  calls: Array<{ name: string; data: unknown }>;
  emit(event: string, data: unknown, seq: number): void;
  answer(name: string, reply: Reply): void;
  request(name: string, data: unknown): Promise<Reply>;
  sampleState(): Record<string, unknown> & { seq: number; obs: Record<string, unknown> };
};

const calls = (page: Page, name: string) => page.evaluate((wanted) => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === wanted).map((call) => call.data), name);
const answer = (page: Page, name: string, reply: Reply) => page.evaluate(([wanted, value]) => (window as unknown as { pampapadFake: Fake }).pampapadFake.answer(wanted as string, value as Reply), [name, reply] as const);

// La foto de las muestras está grabando: Grabar atajo se apaga. Un estado detenido lo enciende. extra cambia otras partes del estado.
const setState = (page: Page, extra: Record<string, unknown> = {}, take = false) =>
  page.evaluate(
    ([more, inTake]) => {
      const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
      const state = fake.sampleState();
      const obs = inTake ? state.obs : { ...state.obs, recording: "stopped", takeInProgress: false, streaming: false, recordedMs: 0 };
      fake.emit("state", { ...state, seq: state.seq + 10, obs, ...(more as object) }, state.seq + 10);
    },
    [extra, take] as const,
  );

// La configuración de las muestras con lo que la prueba necesita encima (otra página, atajos generales, el bloque console).
const withConfig = (page: Page, patch: Record<string, unknown>) =>
  page.evaluate(async (more) => {
    const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
    const reply = (await fake.request("app.snapshot", {})) as { ok: true; data: { config: Record<string, unknown>; fingerprint: string } };
    fake.emit("config.changed", { config: { ...reply.data.config, ...more }, fingerprint: reply.data.fingerprint }, 500);
  }, patch);

const twoPages = (page: Page) =>
  withConfig(page, {
    pages: [
      { id: "grabacion", name: "Grabación", keys: [{ position: 5, label: "Short", action: "marca-short", hotkey: "Ctrl+Shift+Alt+F9" }] },
      { id: "escenas", name: "Escenas", keys: [] },
    ],
  });

type Saved = { config: Record<string, unknown> & { pages: Array<{ id: string }> } };
const lastSaved = async (page: Page) => ((await calls(page, "config.save")) as Saved[]).at(-1)?.config;
const card = (page: Page, name: string) => page.getByRole("region", { name });

test.beforeEach(async ({ page }) => {
  await page.goto("/#/settings");
  await expect(page.getByRole("heading", { level: 1, name: "Ajustes" })).toBeVisible();
});

test("subir una página y elegir la principal mandan config.save con el orden y mainPage", async ({ page }) => {
  await twoPages(page);
  await page.getByRole("button", { name: "Subir Escenas" }).click();
  await card(page, "Páginas que se muestran").getByRole("radio").first().check();
  await expect(page.getByText("Cambios sin guardar en: Biblioteca")).toBeVisible();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  const config = await lastSaved(page);
  expect(config?.pages.map((p) => p.id)).toEqual(["escenas", "grabacion"]);
  expect(config?.mainPage).toBe("escenas");
});

test("agregar un atajo general grabado con la captura, y la combinación repetida se rechaza diciendo dónde", async ({ page }) => {
  await setState(page);
  await page.getByRole("button", { name: "Agregar atajo" }).click();
  const hotkeys = card(page, "Atajos de teclado generales");

  await hotkeys.getByRole("button", { name: "Grabar atajo" }).click();
  await page.keyboard.press("Control+Shift+Alt+F9");
  await expect(page.getByText("Repetida en la tecla 5 de Grabación")).toBeVisible();
  await page.keyboard.press("Control+Alt+B");
  await expect(page.getByLabel("Atajo general 1")).toHaveValue("Ctrl+Alt+B");
  await page.getByLabel("Acción del atajo 1").selectOption("marca-short");
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  expect((await lastSaved(page))?.hotkeys).toEqual([{ hotkey: "Ctrl+Alt+B", action: "marca-short" }]);
  expect(await calls(page, "hotkeys.suspend")).toEqual([{ suspended: true }, { suspended: false }]);
});

// Repaso de la etapa 8, fila 5: "Quitar atajo" al lado de "Quitar" dejaba la fila sin combinación en vez de sacarla.
test("una fila de atajo general tiene un solo Quitar, que la saca entera, y el campo vacío no muestra una combinación", async ({ page }) => {
  await page.getByRole("button", { name: "Agregar atajo" }).click();
  const hotkeys = card(page, "Atajos de teclado generales");

  await expect(page.getByLabel("Atajo general 1")).toHaveAttribute("placeholder", "Sin atajo");
  await expect(hotkeys.getByRole("button", { name: /Quitar/ })).toHaveCount(1);
  await hotkeys.getByRole("button", { name: "Quitar" }).click();
  await expect(page.getByLabel("Atajo general 1")).toHaveCount(0);
});

test("callar el sonido en falso lo escribe en el bloque console", async ({ page }) => {
  await page.getByRole("switch", { name: "Callar el sonido con una toma en curso" }).click();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  expect((await lastSaved(page))?.console).toEqual({ muteSoundWhileRecording: false });
});

test("callar el sonido en verdadero quita el campo, y el bloque si queda vacío, sin tocar lo desconocido", async ({ page }) => {
  await withConfig(page, { console: { muteSoundWhileRecording: false } });
  await page.getByRole("switch", { name: "Callar el sonido con una toma en curso" }).click();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();
  expect(await lastSaved(page)).not.toHaveProperty("console");

  await withConfig(page, { console: { muteSoundWhileRecording: false, inventado: 3 } });
  await page.getByRole("switch", { name: "Callar el sonido con una toma en curso" }).click();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();
  expect((await lastSaved(page))?.console).toEqual({ inventado: 3 });
});

test("el arranque con Windows manda settings.save y la tarjeta muestra lo que devuelve", async ({ page }) => {
  await answer(page, "settings.save", { ok: true, data: { hotkeysEnabled: true, startWithWindows: "off", skippedVersion: "0.7.0", mcpEnabled: true } });
  await page.getByRole("switch", { name: "Arrancar con Windows" }).click();
  await expect(page.getByText("Cambios sin guardar en: Esta PC")).toBeVisible();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  expect(await calls(page, "settings.save")).toEqual([{ startWithWindows: false }]);
  await expect(page.getByRole("switch", { name: "Arrancar con Windows" })).toHaveAttribute("aria-checked", "false");
  await expect(page.getByText("Versión salteada: 0.7.0")).toBeVisible();
});

test("con cambios en los dos lados, Guardar manda los dos, primero la biblioteca, y la barra se limpia", async ({ page }) => {
  await page.getByRole("switch", { name: "Callar el sonido con una toma en curso" }).click();
  await page.getByRole("switch", { name: "Arrancar con Windows" }).click();
  await expect(page.getByText("Cambios sin guardar en: Biblioteca / Esta PC")).toBeVisible();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  await expect(page.getByText("Guardado.")).toBeVisible();
  await expect(page.getByText(/Cambios sin guardar/)).toBeHidden();
  const order = await page.evaluate(() => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.map((call) => call.name).filter((name) => name.endsWith(".save")));
  expect(order).toEqual(["config.save", "settings.save"]);
});

test("con una toma en curso config.save se niega y settings.save pasa: la barra dice cuál y queda el borrador de la biblioteca", async ({ page }) => {
  await answer(page, "config.save", { ok: false, error: { code: "read_only_while_recording", message: "Hay una toma en curso." } });
  await page.getByRole("switch", { name: "Callar el sonido con una toma en curso" }).click();
  await page.getByRole("switch", { name: "Arrancar con Windows" }).click();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  await expect(page.getByText("No se guardó la Biblioteca: Hay una toma en curso.")).toBeVisible();
  await expect(page.getByText("Cambios sin guardar en: Biblioteca", { exact: true })).toBeVisible();
  expect(await calls(page, "settings.save")).toEqual([{ startWithWindows: false }]);
  await expect(page.getByRole("switch", { name: "Callar el sonido con una toma en curso" })).toHaveAttribute("aria-checked", "false");
});

test("el borrador de Esta PC cuenta en window.dirty y sobrevive a ir a Teclas y volver", async ({ page }) => {
  await page.getByRole("switch", { name: "Arrancar con Windows" }).click();
  await expect.poll(async () => (await calls(page, "window.dirty")).at(-1)).toEqual({ dirty: true });

  await page.getByRole("navigation", { name: "Pantallas" }).getByRole("link", { name: /Teclas/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Teclas" })).toBeVisible();
  await page.getByRole("navigation", { name: "Pantallas" }).getByRole("link", { name: /Ajustes/ }).click();

  await expect(page.getByRole("switch", { name: "Arrancar con Windows" })).toHaveAttribute("aria-checked", "false");
  expect((await calls(page, "window.dirty")).at(-1)).toEqual({ dirty: true });
});

test("si settings.save falla y config.save pasa, la barra dice cuál y queda el borrador de Esta PC", async ({ page }) => {
  await answer(page, "settings.save", { ok: false, error: { code: "x", message: "No se pudo." } });
  await page.getByRole("switch", { name: "Callar el sonido con una toma en curso" }).click();
  await page.getByRole("switch", { name: "Arrancar con Windows" }).click();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  await expect(page.getByText("No se guardó Esta PC: No se pudo.")).toBeVisible();
  await expect(page.getByText("Cambios sin guardar en: Esta PC", { exact: true })).toBeVisible();
  expect(await calls(page, "config.save")).toHaveLength(1);
  await expect(page.getByRole("switch", { name: "Arrancar con Windows" })).toHaveAttribute("aria-checked", "false");
});

test("Saltear esta versión manda update.skip y la tarjeta muestra la salteada con el botón para olvidarla", async ({ page }) => {
  await setState(page, { update: { phase: "available", version: "0.7.0" } });
  await page.getByRole("button", { name: "Saltear esta versión" }).click();
  await setState(page, { update: { phase: "idle" } });

  expect(await calls(page, "update.skip")).toEqual([{}]);
  await expect(page.getByText("Versión salteada: 0.7.0")).toBeVisible();
  await expect(page.getByRole("button", { name: "Olvidar la versión salteada" })).toBeVisible();
});

test("la captura que se desmonta en pleno await reanuda los atajos", async ({ page }) => {
  await setState(page);
  await page.getByRole("button", { name: "Agregar atajo" }).click();
  await page.evaluate(() => {
    const fake = (window as unknown as { pampapadFake: Fake & { release?: () => void } }).pampapadFake;
    const original = fake.request.bind(fake);
    fake.request = (name, data) =>
      name === "hotkeys.suspend" && (data as { suspended: boolean }).suspended
        ? new Promise((resolve) => {
            fake.release = () => resolve(original(name, data));
          })
        : original(name, data);
  });
  await card(page, "Atajos de teclado generales").getByRole("button", { name: "Grabar atajo" }).click();
  await page.getByRole("navigation", { name: "Pantallas" }).getByRole("link", { name: /Teclas/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Teclas" })).toBeVisible();
  await page.evaluate(() => (window as unknown as { pampapadFake: { release: () => void } }).pampapadFake.release());

  await expect.poll(() => calls(page, "hotkeys.suspend")).toEqual([{ suspended: true }, { suspended: false }]);
});

test("el reloj de 25 s cierra la captura con el aviso y reanuda", async ({ page }) => {
  await page.clock.install();
  await page.reload();
  await setState(page);
  await page.getByRole("button", { name: "Agregar atajo" }).click();
  await card(page, "Atajos de teclado generales").getByRole("button", { name: "Grabar atajo" }).click();
  await expect(page.getByText("Apretá la combinación")).toBeVisible();

  await page.clock.runFor(25_000);
  await expect(page.getByText("Se acabó el tiempo, volvé a grabar.")).toBeVisible();
  await expect(page.getByText("Apretá la combinación")).toBeHidden();
  expect(await calls(page, "hotkeys.suspend")).toEqual([{ suspended: true }, { suspended: false }]);
});

test("un problema de un atajo general en Estado lleva a su fila en Ajustes", async ({ page }) => {
  await page.goto("/#/status");
  await withConfig(page, { hotkeys: [{ hotkey: "Ctrl+Alt+B", action: "marca-short" }, { hotkey: "Ctrl+Alt+B", action: "marca-inicio" }] });
  await setState(page, { problems: [{ where: "hotkeys[1].hotkey", code: "hotkey_repeated", message: "Ctrl+Alt+B está dos veces.", severity: "error" }] });
  await page.getByRole("link", { name: "hotkeys[1].hotkey" }).click();

  await expect(page).toHaveURL(/#\/settings\?hotkey=1$/);
  await expect(page.locator("#atajo-1")).toHaveAttribute("aria-current", "true");
  await expect(page.locator("#atajo-1")).toBeInViewport();
});

test("con cambios de la biblioteca sin guardar, Recargar se apaga y dice por qué", async ({ page }) => {
  await expect(page.getByRole("button", { name: "Recargar" })).toBeEnabled();
  await page.getByRole("switch", { name: "Callar el sonido con una toma en curso" }).click();

  await expect(page.getByRole("button", { name: "Recargar" })).toBeDisabled();
  await expect(page.getByText("Recargar tira los cambios sin guardar de la biblioteca: primero guardá o descartá.")).toBeVisible();
});

const claude = (page: Page) => card(page, "Claude");
const claudeSwitch = (page: Page) => page.getByRole("switch", { name: "Permitir que Claude maneje PampaPad" });

test("apagar Permitir que Claude maneje PampaPad y guardar manda settings.save con mcpEnabled y la tarjeta muestra lo devuelto", async ({ page }) => {
  await answer(page, "settings.save", { ok: true, data: { hotkeysEnabled: true, startWithWindows: "on", mcpEnabled: false, mcpCommand: "claude mcp add otra" } });
  await expect(claude(page).getByText("Mientras se graba: sólo lectura")).toBeVisible();
  await expect(claudeSwitch(page)).toHaveAttribute("aria-checked", "true");
  await claudeSwitch(page).click();
  await expect(page.getByText("Cambios sin guardar en: Esta PC")).toBeVisible();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  expect(await calls(page, "settings.save")).toEqual([{ mcpEnabled: false }]);
  await expect(claudeSwitch(page)).toHaveAttribute("aria-checked", "false");
  await expect(claude(page).getByText("claude mcp add otra")).toBeVisible();
});

test("Copiar deja en el portapapeles el comando que registra el MCP", async ({ page }) => {
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  const command = claude(page).getByText("claude mcp add --scope user pampapad", { exact: false });
  await expect(command).toBeVisible();

  await claude(page).getByRole("button", { name: "Copiar" }).click();

  await expect(claude(page).getByText("Copiado")).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(await command.textContent());
});

test("sin comando, la tarjeta Claude dice que no está disponible en la copia de desarrollo", async ({ page }) => {
  await answer(page, "settings.save", { ok: true, data: { hotkeysEnabled: true, startWithWindows: "unavailable", mcpEnabled: false } });
  await claudeSwitch(page).click();
  await page.getByRole("button", { name: "Guardar", exact: true }).click();

  await expect(claude(page).getByText("No disponible en la copia de desarrollo")).toBeVisible();
  await expect(claude(page).getByRole("button", { name: "Copiar" })).toHaveCount(0);
});
