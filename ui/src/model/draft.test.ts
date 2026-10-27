import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
// El único cruce de model/ con bridge/, y sólo en una prueba: ata z.looseObject a no perder nada (02 §3, regla 1).
// eslint-disable-next-line no-restricted-imports
import { configSchema } from "../bridge/contract";
import { keyAt, moveStep, newActionId, removeAction, removeStep, sameConfig, scriptAction, setKey, setStep, slug, upsertAction, type ActionLike, type ConfigLike, type PageLike } from "./draft";

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
