import { expect, test, type Page } from "@playwright/test";

// Lo que la página deja en window.pampapadFake cuando corre con el puente falso (src/bridge/bridge.ts).
type Fake = {
  calls: Array<{ name: string; data: unknown }>;
  emit(event: string, data: unknown, seq: number): void;
  answer(name: string, reply: { ok: true; data: unknown } | { ok: false; error: { code: string; message: string; where?: string; problems?: unknown[] } }): void;
  sampleState(): Record<string, unknown> & { seq: number; obs: Record<string, unknown> };
};

type SaveCall = { config: { pages: Array<{ keys: Array<Record<string, unknown>> }>; actions: Array<Record<string, unknown>> }; expectedFingerprint: string };

const calls = (page: Page, name: string) => page.evaluate((wanted) => (window as unknown as { pampapadFake: Fake }).pampapadFake.calls.filter((call) => call.name === wanted).map((call) => call.data), name);

// La huella de la foto, que la pantalla deja en un atributo para que la prueba no la copie a mano.
const fingerprint = (page: Page) => page.locator("[data-fingerprint]").getAttribute("data-fingerprint");

// La foto de las muestras está grabando (Contract.SampleState): Probar y Grabar atajo se apagan. Un estado detenido los enciende.
const stopped = (page: Page) =>
  page.evaluate(() => {
    const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
    const state = fake.sampleState();
    fake.emit("state", { ...state, seq: state.seq + 1, obs: { ...state.obs, recording: "stopped", takeInProgress: false, streaming: false, recordedMs: 0 } }, state.seq + 1);
  });

test.beforeEach(async ({ page }) => {
  await page.goto("/#/keys");
  await expect(page.getByRole("heading", { level: 1, name: "Teclas" })).toBeVisible();
});

test("abre en Teclas, con la grilla de 12 y la etiqueta corta de la tecla 5", async ({ page }) => {
  await expect(page.getByRole("button", { name: /^Tecla \d+$/ })).toHaveCount(12);
  await expect(page.getByRole("button", { name: "Tecla 5", exact: true })).toContainText("SHORT");
  await expect(page.getByRole("button", { name: "Tecla 1", exact: true })).toContainText("—");
  await expect(page.getByRole("button", { name: "Grabación" })).toContainText("principal");
});

test("elegir una tecla vacía, ponerle etiqueta y acción, y guardar manda la configuración entera con la huella de la foto", async ({ page }) => {
  await page.getByRole("button", { name: "Tecla 1", exact: true }).click();
  await page.getByLabel("Etiqueta").fill("Banco");
  await page.getByLabel("Qué hace").selectOption("marca-short");
  await expect(page.getByRole("button", { name: "Tecla 1", exact: true })).toContainText("BANCO");
  await page.getByRole("button", { name: "Guardar" }).click();

  await expect(page.getByText("Guardado.")).toBeVisible();
  const saves = (await calls(page, "config.save")) as SaveCall[];
  expect(saves).toHaveLength(1);
  expect(saves[0].expectedFingerprint).toBe(await fingerprint(page));
  expect(saves[0].config.pages[0].keys[0]).toEqual({ position: 1, label: "Banco", action: "marca-short" });
  expect(saves[0].config.pages[0].keys).toHaveLength(3);
  expect(await calls(page, "window.dirty")).toEqual(expect.arrayContaining([{ dirty: true }, { dirty: false }]));
});

test("el borrador sobrevive a cambiar de pantalla y window.dirty no vuelve a false al navegar", async ({ page }) => {
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByLabel("Etiqueta").fill("Corto");
  await expect(page.getByText("cambios sin guardar")).toBeVisible();
  const before = ((await calls(page, "window.dirty")) as Array<{ dirty: boolean }>).length;

  await page.getByRole("navigation", { name: "Pantallas" }).getByRole("link", { name: /Acciones/ }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Acciones" })).toBeVisible();
  await page.getByRole("navigation", { name: "Pantallas" }).getByRole("link", { name: /Teclas/ }).click();
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();

  await expect(page.getByLabel("Etiqueta")).toHaveValue("Corto");
  await expect(page.getByText("cambios sin guardar")).toBeVisible();
  const after = (await calls(page, "window.dirty")) as Array<{ dirty: boolean }>;
  expect(after.slice(before)).not.toContainEqual({ dirty: false });
});

test("grabar un atajo suspende los globales, toma la combinación del teclado y los reanuda", async ({ page }) => {
  await stopped(page);
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByRole("button", { name: "Grabar atajo" }).click();
  await expect(page.getByText("Apretá la combinación")).toBeVisible();
  await page.keyboard.press("Control+Shift+Alt+F8");

  await expect(page.getByLabel("Atajo de teclado")).toHaveValue("Ctrl+Shift+Alt+F8");
  expect(await calls(page, "hotkeys.suspend")).toEqual([{ suspended: true }, { suspended: false }]);
});

test("Esc cancela la captura, reanuda los globales y deja el atajo como estaba", async ({ page }) => {
  await stopped(page);
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByRole("button", { name: "Grabar atajo" }).click();
  await page.keyboard.press("Escape");

  await expect(page.getByLabel("Atajo de teclado")).toHaveValue("Ctrl+Shift+Alt+F9");
  expect(await calls(page, "hotkeys.suspend")).toEqual([{ suspended: true }, { suspended: false }]);
});

test("salir de la pantalla en plena captura reanuda los globales", async ({ page }) => {
  await stopped(page);
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByRole("button", { name: "Grabar atajo" }).click();
  await expect(page.getByText("Apretá la combinación")).toBeVisible();
  // Un cambio de hash y no un click: el click ya dispara onBlur y taparía la limpieza del desmontaje.
  await page.evaluate(() => {
    window.location.hash = "#/status";
  });

  await expect(page.getByRole("heading", { level: 1, name: "Estado" })).toBeVisible();
  await expect.poll(() => calls(page, "hotkeys.suspend")).toEqual([{ suspended: true }, { suspended: false }]);
});

test("si suspender falla, la captura no empieza y la caja dice por qué", async ({ page }) => {
  await stopped(page);
  await page.evaluate(() =>
    (window as unknown as { pampapadFake: Fake }).pampapadFake.answer("hotkeys.suspend", { ok: false, error: { code: "read_only_while_recording", message: "Hay una toma en curso." } }),
  );
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByRole("button", { name: "Grabar atajo" }).click();

  await expect(page.getByText("Hay una toma en curso.")).toBeVisible();
  await expect(page.getByText("Apretá la combinación")).toHaveCount(0);
  expect(await calls(page, "hotkeys.suspend")).toEqual([{ suspended: true }]);
});

test("una tecla sola se rechaza con el motivo y la captura sigue", async ({ page }) => {
  await stopped(page);
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByRole("button", { name: "Grabar atajo" }).click();
  await page.keyboard.press("F8");

  await expect(page.getByText("Hace falta por lo menos un modificador")).toBeVisible();
  await page.keyboard.press("Control+Alt+KeyB");
  await expect(page.getByLabel("Atajo de teclado")).toHaveValue("Ctrl+Alt+B");
});

test("una combinación que ya usa otra tecla se rechaza diciendo dónde y la captura sigue", async ({ page }) => {
  await stopped(page);
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByRole("button", { name: "Grabar atajo" }).click();
  await page.keyboard.press("Control+Shift+Alt+F6");

  await expect(page.getByText("Repetida en la tecla 6 de Grabación")).toBeVisible();
  await expect(page.getByLabel("Atajo de teclado")).toHaveValue("Ctrl+Shift+Alt+F9");
  await page.keyboard.press("Control+Shift+Alt+F8");
  await expect(page.getByLabel("Atajo de teclado")).toHaveValue("Ctrl+Shift+Alt+F8");
});

test("elegir un script deja en la tecla una acción de un paso script.run", async ({ page }) => {
  await page.evaluate(() => (window as unknown as { pampapadFake: Fake }).pampapadFake.answer("options.list", { ok: true, data: { items: [{ name: "bancocam", detail: "Cámara del banco" }] } }));
  // La pantalla pide los scripts al abrirse: ir a Estado y volver la abre de nuevo, ya con la respuesta nueva.
  await page.getByRole("navigation", { name: "Pantallas" }).getByRole("link", { name: /Estado/ }).click();
  await page.getByRole("navigation", { name: "Pantallas" }).getByRole("link", { name: /Teclas/ }).click();
  await page.getByRole("button", { name: "Tecla 3", exact: true }).click();
  await page.getByLabel("Qué hace").selectOption("script:bancocam");
  await page.getByRole("button", { name: "Guardar" }).click();

  const saves = (await calls(page, "config.save")) as SaveCall[];
  expect(saves[0].config.pages[0].keys[0]).toEqual({ position: 3, action: "script-bancocam" });
  expect(saves[0].config.actions.at(-1)).toEqual({ id: "script-bancocam", name: "Cámara del banco", steps: [{ type: "script.run", script: "bancocam" }] });
});

test("lo que se escribe mientras guarda no se pierde: queda como cambio sin guardar", async ({ page }) => {
  // El puente falso contesta en el acto: se demora config.save para escribir en el medio.
  await page.evaluate(() => {
    const fake = (window as unknown as { pampapadFake: { request(name: string, data: unknown): Promise<unknown> } }).pampapadFake;
    const reply = fake.request.bind(fake);
    fake.request = (name, data) => (name === "config.save" ? new Promise((done) => setTimeout(() => done(reply(name, data)), 500)) : reply(name, data));
  });
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByLabel("Etiqueta").fill("Corto");
  await page.getByRole("button", { name: "Guardar" }).click();
  await expect(page.getByText("Guardando…")).toBeVisible();
  await page.getByLabel("Etiqueta").fill("Corto 2");

  await expect(page.getByText("Guardado.")).toBeVisible();
  await expect(page.getByLabel("Etiqueta")).toHaveValue("Corto 2");
  await expect(page.getByText("cambios sin guardar")).toBeVisible();
});

test("un guardado que falla se muestra, con cada problema y su ubicación", async ({ page }) => {
  await page.evaluate(() =>
    (window as unknown as { pampapadFake: Fake }).pampapadFake.answer("config.save", {
      ok: false,
      error: {
        code: "config_invalid",
        message: "La tecla 5 de Grabación para o pausa la grabación con un toque.",
        where: "pages[0].keys[0].action",
        problems: [{ where: "pages[0].keys[0].action", code: "hold_required", message: "La tecla 5 de Grabación para o pausa la grabación con un toque.", severity: "error" }],
      },
    }),
  );
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByLabel("Etiqueta").fill("Corto");
  await page.getByRole("button", { name: "Guardar" }).click();

  const alert = page.getByRole("alert").filter({ hasText: "No se guardó" });
  await expect(alert).toContainText("para o pausa la grabación con un toque");
  await expect(alert).toContainText("pages[0].keys[0].action");
  await expect(page.getByRole("button", { name: "Guardar" })).toBeEnabled();
});

test("si cambió en el disco, lo dice y ofrece recargar la biblioteca", async ({ page }) => {
  await page.evaluate(() =>
    (window as unknown as { pampapadFake: Fake }).pampapadFake.answer("config.save", {
      ok: false,
      error: { code: "config_changed_on_disk", message: "config.json cambió en el disco desde que se leyó.", where: "config.json" },
    }),
  );
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByLabel("Etiqueta").fill("Corto");
  await page.getByRole("button", { name: "Guardar" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "cambió en el disco" })).toBeVisible();
  await page.getByRole("button", { name: "Recargar biblioteca" }).click();

  expect(await calls(page, "library.reload")).toEqual([{}]);
  await expect(page.getByLabel("Etiqueta")).toHaveValue("Short");
});

test("una foto nueva con el borrador sucio no lo pisa: avisa, guarda con la huella de la base y deja descartarlo", async ({ page }) => {
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByLabel("Etiqueta").fill("Corto");
  const base = await fingerprint(page);
  await page.evaluate(() =>
    (window as unknown as { pampapadFake: Fake }).pampapadFake.emit("config.changed", { config: { version: 1, pages: [], actions: [] }, fingerprint: "nueva" }, 99),
  );

  await expect(page.getByText("La biblioteca cambió")).toBeVisible();
  await expect(page.getByLabel("Etiqueta")).toHaveValue("Corto");
  // El programa rechazaría ese guardado (config_changed_on_disk); lo que importa acá es qué huella manda la página.
  await page.evaluate(() =>
    (window as unknown as { pampapadFake: Fake }).pampapadFake.answer("config.save", { ok: false, error: { code: "config_changed_on_disk", message: "config.json cambió en el disco desde que se leyó." } }),
  );
  await page.getByRole("button", { name: "Guardar" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "No se guardó" })).toBeVisible();
  const saves = (await calls(page, "config.save")) as SaveCall[];
  expect(saves).toHaveLength(1);
  expect(saves[0].expectedFingerprint).toBe(base);
  expect(base).not.toBe("nueva");

  await page.getByRole("button", { name: "Descartar y tomar la nueva" }).click();
  await expect(page.getByText("La biblioteca cambió")).toHaveCount(0);
  await expect(page.getByText("cambios sin guardar")).toHaveCount(0);
  await expect(page.locator("[data-fingerprint='nueva']")).toBeVisible();
});

test("Probar corre la acción de la tecla como borrador y muestra lo que llega por action.finished", async ({ page }) => {
  await stopped(page);
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.getByRole("button", { name: "Probar" }).click();

  const tests = (await calls(page, "action.test")) as Array<{ action: { id: string } }>;
  expect(tests.map((call) => call.action.id)).toEqual(["marca-short"]);
  await page.evaluate(() =>
    (window as unknown as { pampapadFake: Fake }).pampapadFake.emit(
      "action.finished",
      { actionId: "marca-short", runId: "0f8fad5b-d9cb-469f-a165-70867728950e", requestedBy: "window", ok: true, durationMs: 18, steps: [{ outcome: "ok" }] },
      43,
    ),
  );
  await expect(page.getByRole("list", { name: "Resultado de la prueba" })).toContainText("bien");
});

test("con una toma en curso, Probar y Grabar atajo se apagan y dicen por qué", async ({ page }) => {
  await page.getByRole("button", { name: "Tecla 5", exact: true }).click();
  await page.evaluate(() => {
    const fake = (window as unknown as { pampapadFake: Fake }).pampapadFake;
    const state = fake.sampleState();
    fake.emit("state", { ...state, seq: state.seq + 1, obs: { ...state.obs, recording: "paused", takeInProgress: true } }, state.seq + 1);
  });

  await expect(page.getByRole("button", { name: "Probar" })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Grabar atajo" })).toBeDisabled();
  await expect(page.getByText("mientras grabás, estás en pausa o transmitís").first()).toBeVisible();
});
