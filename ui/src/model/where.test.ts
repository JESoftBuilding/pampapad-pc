import { describe, expect, it } from "vitest";
import { linkFor, queryOf, whereOfAction, whereOfKey } from "./where";

const config = {
  pages: [{ id: "grabacion", keys: [{ position: 5 }, { position: 12 }] }, { id: "escenas", keys: [] }],
  actions: [{ id: "marca-short" }, { id: "ir-al-banco" }],
};

describe("linkFor", () => {
  it("una ubicación dentro de una tecla lleva a Teclas, con su página y su posición", () => {
    expect(linkFor("pages[0].keys[1].hotkey", config)).toBe("#/keys?page=grabacion&position=12");
    expect(linkFor("pages[0].keys[0].action", config)).toBe("#/keys?page=grabacion&position=5");
  });

  it("una ubicación dentro de una acción lleva a Acciones, con su id", () => {
    expect(linkFor("actions[1].steps[0].target", config)).toBe("#/actions?id=ir-al-banco");
    expect(linkFor("actions[0].name", config)).toBe("#/actions?id=marca-short");
  });

  it("lo que no es una tecla ni una acción, o apunta a algo que no está, no tiene enlace", () => {
    expect(linkFor("hotkeys[0].hotkey", config)).toBeUndefined();
    expect(linkFor("pages[0].name", config)).toBeUndefined();
    expect(linkFor("pages[7].keys[0].action", config)).toBeUndefined();
    expect(linkFor("actions[9].id", config)).toBeUndefined();
    expect(linkFor("", config)).toBeUndefined();
  });

  it("queryOf lee lo que sigue al signo de pregunta del hash", () => {
    expect(queryOf("#/keys?page=grabacion&position=5").get("position")).toBe("5");
    expect(queryOf("#/keys").get("page")).toBeNull();
  });

  it("y al revés: la ubicación de una tecla o de una acción en el archivo cargado, para buscar sus problemas", () => {
    expect(whereOfKey(config, "grabacion", 12)).toBe("pages[0].keys[1]");
    expect(whereOfKey(config, "grabacion", 7)).toBeUndefined();
    expect(whereOfKey(config, "nadie", 1)).toBeUndefined();
    expect(whereOfAction(config, "ir-al-banco")).toBe("actions[1]");
    expect(whereOfAction(config, "nadie")).toBeUndefined();
  });
});

describe("linkFor de un atajo general", () => {
  it("hotkeys[1].hotkey lleva a la tarjeta de atajos generales de Ajustes, con su índice", () => {
    const withHotkeys = { ...config, hotkeys: [{ hotkey: "Ctrl+Alt+B" }, { hotkey: "Ctrl+Alt+P" }] };
    expect(linkFor("hotkeys[1].hotkey", withHotkeys)).toBe("#/settings?hotkey=1");
    expect(linkFor("hotkeys[5].hotkey", withHotkeys)).toBeUndefined();
  });
});
