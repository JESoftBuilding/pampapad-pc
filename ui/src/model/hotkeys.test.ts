import { describe, expect, it } from "vitest";
import { fromKeyPress, parseHotkey, usedBy, usedByGeneral, type KeyPress } from "./hotkeys";

function press(code: string, modifiers: Partial<KeyPress> = {}): KeyPress {
  return { key: code, code, ctrlKey: false, shiftKey: false, altKey: false, metaKey: false, ...modifiers };
}

describe("fromKeyPress", () => {
  it("arma el texto en el orden Ctrl, Shift, Alt y la tecla, como Hotkey.ToString", () => {
    expect(fromKeyPress(press("F8", { altKey: true, shiftKey: true, ctrlKey: true }))).toEqual({ kind: "hotkey", text: "Ctrl+Shift+Alt+F8" });
    expect(fromKeyPress(press("KeyB", { ctrlKey: true, altKey: true }))).toEqual({ kind: "hotkey", text: "Ctrl+Alt+B" });
    expect(fromKeyPress(press("Digit1", { shiftKey: true }))).toEqual({ kind: "hotkey", text: "Shift+1" });
    expect(fromKeyPress(press("F24", { ctrlKey: true }))).toEqual({ kind: "hotkey", text: "Ctrl+F24" });
  });

  it("con Shift+1 usa el código de la tecla y no el carácter que escribe", () => {
    expect(fromKeyPress({ ...press("Digit1", { shiftKey: true }), key: "!" })).toEqual({ kind: "hotkey", text: "Shift+1" });
  });

  it("un modificador solo es una captura a medias, no un rechazo", () => {
    expect(fromKeyPress(press("ControlLeft", { ctrlKey: true }))).toEqual({ kind: "pending" });
    expect(fromKeyPress(press("ShiftRight", { shiftKey: true }))).toEqual({ kind: "pending" });
    expect(fromKeyPress(press("AltLeft", { altKey: true }))).toEqual({ kind: "pending" });
  });

  it("rechaza una tecla sola, la tecla Windows y lo que no es letra, número ni F1 a F24 (02 §4.3)", () => {
    expect(fromKeyPress(press("F8")).kind).toBe("rejected");
    expect(fromKeyPress(press("KeyA", { metaKey: true, ctrlKey: true })).kind).toBe("rejected");
    expect(fromKeyPress(press("Space", { ctrlKey: true })).kind).toBe("rejected");
    expect(fromKeyPress(press("Enter", { ctrlKey: true, shiftKey: true })).kind).toBe("rejected");
    expect(fromKeyPress(press("ArrowUp", { altKey: true })).kind).toBe("rejected");
    expect(fromKeyPress(press("F25", { ctrlKey: true })).kind).toBe("rejected");
  });
});

describe("parseHotkey", () => {
  it("lee sin distinguir mayúsculas y escribe en el orden canónico, como Hotkey.Parse", () => {
    expect(parseHotkey("alt+ctrl+f9")).toBe("Ctrl+Alt+F9");
    expect(parseHotkey("Ctrl+Shift+Alt+B")).toBe("Ctrl+Shift+Alt+B");
    expect(parseHotkey(" shift + 7 ")).toBe("Shift+7");
  });

  it("devuelve null con una tecla sola, Win, un modificador repetido, F0, F25 o una letra con acento", () => {
    expect(parseHotkey("F9")).toBeNull();
    expect(parseHotkey("Win+F9")).toBeNull();
    expect(parseHotkey("Ctrl+Ctrl+F9")).toBeNull();
    expect(parseHotkey("Ctrl+F0")).toBeNull();
    expect(parseHotkey("Ctrl+F25")).toBeNull();
    expect(parseHotkey("Ctrl+Ñ")).toBeNull();
    expect(parseHotkey("")).toBeNull();
  });
});

// 05 §6, tabla de conflictos, fila 1: la combinación que ya usa otra tecla de la misma página o un atajo general (como el validador).
describe("usedBy", () => {
  const config = {
    pages: [
      { id: "grabacion", name: "Grabación", keys: [{ position: 5, hotkey: "Ctrl+Alt+P" }, { position: 6, hotkey: "Ctrl+Shift+Alt+D" }] },
      { id: "sala", name: "Sala", keys: [{ position: 1, hotkey: "Ctrl+Alt+K" }] },
    ],
    hotkeys: [{ hotkey: "Ctrl+Shift+Alt+G", action: "grabar" }],
  };

  it("dice qué otra tecla de la misma página ya la usa, y una tecla de otra página no cuenta", () => {
    expect(usedBy(config, "grabacion", 5, "Ctrl+Shift+Alt+D")).toBe("la tecla 6 de Grabación");
    expect(usedBy(config, "grabacion", 5, "Ctrl+Alt+K")).toBeNull();
  });

  it("dice si la usa un atajo general", () => {
    expect(usedBy(config, "sala", 1, "Ctrl+Shift+Alt+G")).toBe("un atajo general");
  });

  it("la misma tecla no cuenta contra sí misma", () => {
    expect(usedBy(config, "grabacion", 5, "Ctrl+Alt+P")).toBeNull();
    expect(usedBy(config, "grabacion", 5, "Ctrl+Alt+Q")).toBeNull();
  });

  it("escrita distinto pero igual cuenta: se compara con la misma normalización de la gramática", () => {
    expect(usedBy(config, "grabacion", 5, "ctrl+alt+shift+d")).toBe("la tecla 6 de Grabación");
    expect(usedBy(config, "grabacion", 5, "alt+shift+ctrl+g")).toBe("un atajo general");
  });
});

describe("usedByGeneral", () => {
  const config = {
    pages: [
      { id: "grabacion", name: "Grabación", keys: [{ position: 3, hotkey: "Ctrl+Alt+M" }] },
      { id: "sala", name: "Sala", keys: [{ position: 6, hotkey: "Ctrl+Shift+L" }] },
    ],
    hotkeys: [{ hotkey: "Ctrl+Alt+B" }, { hotkey: "Ctrl+Alt+P" }],
  };

  it("encuentra una tecla de cualquier página", () => {
    expect(usedByGeneral(config, 0, "ctrl+shift+l")).toBe("la tecla 6 de Sala");
    expect(usedByGeneral(config, 0, "Ctrl+Alt+M")).toBe("la tecla 3 de Grabación");
  });

  it("encuentra otro atajo general, y el que se edita no cuenta contra sí mismo", () => {
    expect(usedByGeneral(config, 0, "Ctrl+Alt+P")).toBe("el atajo general 2");
    expect(usedByGeneral(config, 1, "Ctrl+Alt+P")).toBeNull();
    expect(usedByGeneral(config, 2, "Ctrl+Alt+F1")).toBeNull();
  });
});
