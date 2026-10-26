import { describe, expect, it } from "vitest";
import { usageOf, usageText } from "./usage";

const config = {
  pages: [
    { id: "grabacion", name: "Grabación", keys: [{ position: 12, label: "Grabar", action: "grabar", onHold: { action: "parar" } }, { position: 3, action: "ir-al-banco" }] },
    { id: "sala", name: "Sala", keys: [{ position: 1, action: "grabar" }] },
  ],
  hotkeys: [{ hotkey: "Ctrl+Shift+Alt+D", action: "parar" }, { hotkey: "Ctrl+Shift+Alt+G", action: "grabar" }],
};

describe("usageOf", () => {
  it("lista cada tecla, cada mantenido y cada atajo general que nombra la acción", () => {
    expect(usageOf(config, "grabar")).toEqual([
      { kind: "key", pageId: "grabacion", pageName: "Grabación", position: 12, hold: false },
      { kind: "key", pageId: "sala", pageName: "Sala", position: 1, hold: false },
      { kind: "hotkey", index: 1, hotkey: "Ctrl+Shift+Alt+G" },
    ]);
    expect(usageOf(config, "parar")).toEqual([
      { kind: "key", pageId: "grabacion", pageName: "Grabación", position: 12, hold: true },
      { kind: "hotkey", index: 0, hotkey: "Ctrl+Shift+Alt+D" },
    ]);
    expect(usageOf(config, "nadie")).toEqual([]);
  });

  it("se lee en castellano", () => {
    expect(usageText({ kind: "key", pageId: "grabacion", pageName: "Grabación", position: 12, hold: true })).toBe("Grabación, tecla 12 al mantener");
    expect(usageText({ kind: "key", pageId: "g", pageName: "", position: 3, hold: false })).toBe("g, tecla 3");
    expect(usageText({ kind: "hotkey", index: 0, hotkey: "Ctrl+Shift+Alt+D" })).toBe("atajo general Ctrl+Shift+Alt+D");
  });
});
