import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
// El único cruce de model/ con bridge/, y sólo en una prueba: ata z.looseObject a no perder nada (02 §3, regla 1).
// eslint-disable-next-line no-restricted-imports
import { configSchema } from "../bridge/contract";
import {
  alertChannel,
  consoleValue,
  keyAt,
  moveStep,
  newActionId,
  removeAction,
  removeStep,
  resetConsole,
  sameConfig,
  scriptAction,
  setAlertChannel,
  setConsoleValue,
  setKey,
  setModuleLayer,
  setMuteSound,
  setStep,
  slug,
  upsertAction,
  type ActionLike,
  type ConfigLike,
  type PageLike,
} from "./draft";

// El archivo mínimo con un campo inventado en cada nivel (02 §3, regla 1), el mismo de ModelTests y LibrarySaveTests.
type Minimal = ConfigLike & { pages: PageLike[]; actions: ActionLike[] };
const minimalPath = fileURLToPath(new URL("../../../tests/PampaPad.Tests/Data/config.minimal.json", import.meta.url));
const minimal = JSON.parse(readFileSync(minimalPath, "utf8")) as Minimal;
const count = (value: unknown) => JSON.stringify(value).split("inventado").length - 1;

describe("setKey", () => {
  it("cambia sólo lo que se le pide y conserva cada campo inventado de todos los niveles", () => {
    const edited = setKey(minimal, "grabacion", 5, { label: "Corto" });
    const parsed = configSchema.parse(minimal);

    expect(keyAt(edited.pages[0], 5)).toMatchObject({ position: 5, label: "Corto", action: "marca-short", inventado: "en la tecla" });
    expect(count(edited)).toBe(count(minimal));
    expect(edited.actions).toBe(minimal.actions);   // lo que no se tocó es el mismo objeto
    expect(count(minimal)).toBe(13);
    expect(parsed).toEqual(minimal);   // el contrato tipado tampoco pierde nada al leerlo
    expect(count(parsed)).toBe(13);
  });

  it("crea la tecla si la posición estaba vacía, y la deja ordenada por posición", () => {
    const edited = setKey(minimal, "grabacion", 1, { label: "Banco", action: "ir-al-banco" });

    expect(edited.pages[0].keys!.map((key) => key.position)).toEqual([1, 5]);
    expect(keyAt(edited.pages[0], 1)).toEqual({ position: 1, label: "Banco", action: "ir-al-banco" });
  });

  it("un campo en undefined se quita del archivo: nunca se escribe null (02 §3, regla 2)", () => {
    const edited = setKey(minimal, "grabacion", 5, { hotkey: undefined, onHold: undefined });
    const key = keyAt(edited.pages[0], 5)!;

    expect("hotkey" in key).toBe(false);
    expect("onHold" in key).toBe(false);
    expect(JSON.stringify(key)).not.toContain("null");
  });

  it("una página que no existe deja la configuración como estaba", () => {
    expect(setKey(minimal, "otra", 1, { label: "x" })).toBe(minimal);
  });
});

describe("acciones", () => {
  it("el id sale del nombre con la regla de 02 §4.2 y no repite uno que ya está", () => {
    expect(slug("Ir al banco")).toBe("ir-al-banco");
    expect(slug("  Cámara (zoom) #2 ")).toBe("camara-zoom-2");
    expect(slug("main")).toBe("main-1");
    expect(slug("x".repeat(50))).toHaveLength(40);
    expect(newActionId(minimal, "Marca: short")).toBe("marca-short-2");
    expect(newActionId(minimal, "")).toBe("accion");
  });

  it("elegir un script deja una acción de un paso script.run (05 §4.1)", () => {
    expect(scriptAction("script-bancocam", "bancocam", "Cámara del banco")).toEqual({
      id: "script-bancocam",
      name: "Cámara del banco",
      steps: [{ type: "script.run", script: "bancocam" }],
    });
  });

  it("upsert reemplaza por id o agrega al final, y remove la saca sin tocar lo demás", () => {
    const renamed = upsertAction(minimal, { id: "marca-short", name: "Marca corta", steps: [{ type: "obs.marker", marker: "short" }] });
    const added = upsertAction(minimal, { id: "nueva", name: "Nueva", steps: [] });
    const removed = removeAction(minimal, "marca-largo");

    expect(renamed.actions.map((action) => action.name)).toEqual(["Marca corta", "Marca: largo"]);
    expect(added.actions.map((action) => action.id)).toEqual(["marca-short", "marca-largo", "nueva"]);
    expect(removed.actions.map((action) => action.id)).toEqual(["marca-short"]);
    expect(count(removed)).toBe(count(minimal));
    expect(removed.pages).toBe(minimal.pages);
  });
});

describe("pasos", () => {
  const action = { id: "a", name: "A", steps: [{ type: "uno", onlyIf: { type: "recording.is", state: "stopped" } }, { type: "dos" }, { type: "tres" }] };

  it("mover lleva el paso con su condición pegada (05 §4.2)", () => {
    expect(moveStep(action, 0, 2).steps!.map((step) => step.type)).toEqual(["dos", "tres", "uno"]);
    expect(moveStep(action, 0, 2).steps![2]).toBe(action.steps[0]);
    expect(moveStep(action, 2, 0).steps!.map((step) => step.type)).toEqual(["tres", "uno", "dos"]);
    expect(moveStep(action, 0, -1)).toBe(action);
    expect(moveStep(action, 0, 3)).toBe(action);
  });

  it("borrar y reemplazar tocan sólo ese lugar", () => {
    expect(removeStep(action, 1).steps!.map((step) => step.type)).toEqual(["uno", "tres"]);
    expect(setStep(action, 1, { type: "dos", x: 1 }).steps![1]).toEqual({ type: "dos", x: 1 });
    expect(setStep(action, 1, { type: "dos", x: 1 }).steps![0]).toBe(action.steps[0]);
  });
});

describe("sameConfig", () => {
  it("compara el contenido, no la referencia", () => {
    expect(sameConfig(minimal, JSON.parse(JSON.stringify(minimal)))).toBe(true);
    expect(sameConfig(minimal, setKey(minimal, "grabacion", 5, { label: "Otra" }))).toBe(false);
  });
});

describe("los ajustes de la consola", () => {
  const bare: ConfigLike = { version: 1, pages: [{ id: "grabacion" }] };

  it("un valor distinto del de fábrica se escribe y el de fábrica se quita, con su objeto y el bloque si quedan vacíos", () => {
    const dim = setConsoleValue(bare, "screen.brightness", 40);
    expect(dim.console).toEqual({ screen: { brightness: 40 } });
    expect(consoleValue(dim, "screen.brightness")).toBe(40);

    const back = setConsoleValue(dim, "screen.brightness", 80);
    expect(back).not.toHaveProperty("console");
    expect(sameConfig(back, bare)).toBe(true);   // lo no tocado no se escribe: el borrador vuelve a ser el archivo
  });

  it("lo de fábrica sin tocar no se escribe, y lo leído falta: vale el de 02 §4.10", () => {
    expect(setConsoleValue(bare, "holdMs", 1000)).toEqual(bare);
    expect(consoleValue(bare, "screen.offAfterSec")).toBe(300);
    expect(consoleValue(bare, "strip.brightness")).toBe(25);
    expect(consoleValue(bare, "sleepAfterSec")).toBe(0);
  });

  it("quitar un valor no pisa un nivel que no es objeto, y un valor del tipo equivocado vale el de fábrica", () => {
    const odd: ConfigLike = { ...bare, console: 3 };
    expect(setConsoleValue(odd, "holdMs", 1000)).toEqual(odd);
    expect(consoleValue({ ...bare, console: { holdMs: "x" } }, "holdMs")).toBe(1000);
  });

  it("Nunca es 0 y se escribe, salvo en dormir, que de fábrica ya es nunca", () => {
    expect(setConsoleValue(bare, "screen.dimAfterSec", 0).console).toEqual({ screen: { dimAfterSec: 0 } });
    expect(setConsoleValue(bare, "sleepAfterSec", 0)).not.toHaveProperty("console");
  });

  it("conserva lo inventado del bloque y de sus objetos", () => {
    const edited = setConsoleValue(minimal, "screen.brightness", 80);
    expect(edited.console).toEqual({ ...(minimal.console as object), screen: { inventado: "en screen" } });
    expect(count(setConsoleValue(minimal, "strip.brightness", 60))).toBe(count(minimal));
  });

  it("callar el sonido sigue la misma regla: verdadero es de fábrica", () => {
    expect(setMuteSound(bare, false).console).toEqual({ muteSoundWhileRecording: false });
    expect(setMuteSound(setMuteSound(bare, false), true)).toEqual(bare);
  });

  it("un canal de aviso: los graves vienen con luz y sonido, no_pc también (0.2.1)", () => {
    expect(alertChannel(bare, "mic_muted", "sound")).toBe(true);
    expect(alertChannel(bare, "no_pc", "light")).toBe(true);
    expect(alertChannel(bare, "no_pc", "sound")).toBe(true);

    const quiet = setAlertChannel(bare, "mic_muted", "sound", false);
    expect(quiet.console).toEqual({ alerts: { mic_muted: { sound: false } } });
    expect(alertChannel(quiet, "mic_muted", "sound")).toBe(false);
    expect(setAlertChannel(quiet, "mic_muted", "sound", true)).toEqual(bare);
    expect(setAlertChannel(bare, "no_pc", "sound", false).console).toEqual({ alerts: { no_pc: { sound: false } } });
  });

  it("un canal vuelto a fábrica deja lo inventado del aviso", () => {
    const edited = setAlertChannel(minimal, "marker_failed", "sound", true);
    expect((edited.console as { alerts: unknown }).alerts).toEqual({ marker_failed: { light: true, inventado: "en el aviso" } });
  });

  it("la capa de la tecla del módulo se elige y se quita, y moduleKey vacío desaparece", () => {
    const chosen = setModuleLayer(bare, "grabacion");
    expect(chosen.moduleKey).toEqual({ layer: "grabacion" });
    expect(setModuleLayer(chosen, null)).toEqual(bare);
    expect(setModuleLayer(minimal, null).moduleKey).toEqual({ inventado: "en moduleKey" });
  });

  it("volver a los valores por defecto quita el bloque console entero y nada más", () => {
    const reset = resetConsole(minimal);
    expect("console" in reset).toBe(false);
    expect({ ...reset, console: minimal.console }).toEqual(minimal);
    expect(reset.moduleKey).toBe(minimal.moduleKey);
    expect(reset.pages).toBe(minimal.pages);
  });
});
