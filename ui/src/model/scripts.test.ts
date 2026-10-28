import { describe, expect, it } from "vitest";
import { countText, filterScripts, lastRunText, sortScripts, type ScriptLike } from "./scripts";

function script(id: string, extra: Partial<ScriptLike> = {}): ScriptLike {
  return { id, name: id, tags: [], status: "ready", usedBy: [], ...extra };
}

const used = [{ action: "ir-al-banco", actionName: "Ir al banco" }];
const list = [
  script("bancocam", { name: "Cámara de banco", description: "Prende la cámara", tags: ["camara"], usedBy: used, lastRun: { at: "2026-10-27T10:00:00Z", end: { ok: true, durationMs: 9000 } } }),
  script("luces", { name: "Luces del taller", tags: ["sala"], usedBy: [{ ...used[0], action: "luces", actionName: "Prender luces" }], lastRun: { at: "2026-10-27T11:00:00Z", end: { ok: false, durationMs: 100 } } }),
  script("roto", { status: "problem" }),
  script("suelto.cmd", { status: "noManifest" }),
];

describe("filterScripts", () => {
  it("busca en nombre, id, descripción y nombre de las acciones que lo usan, sin mayúsculas ni tildes", () => {
    expect(filterScripts(list, { text: "camara" }).map((s) => s.id)).toEqual(["bancocam"]);
    expect(filterScripts(list, { text: "PRENDE" }).map((s) => s.id)).toEqual(["bancocam", "luces"]);
    expect(filterScripts(list, { text: "ir al banco" }).map((s) => s.id)).toEqual(["bancocam"]);
    expect(filterScripts(list, { text: "suelto" }).map((s) => s.id)).toEqual(["suelto.cmd"]);
  });

  it("filtra por etiqueta", () => {
    expect(filterScripts(list, { tag: "sala" }).map((s) => s.id)).toEqual(["luces"]);
  });

  it("filtra por situación: todos, en uso, sin usar, con problema y sin ficha", () => {
    expect(filterScripts(list, { situation: "all" })).toHaveLength(4);
    expect(filterScripts(list, { situation: "used" }).map((s) => s.id)).toEqual(["bancocam", "luces"]);
    expect(filterScripts(list, { situation: "unused" }).map((s) => s.id)).toEqual(["roto", "suelto.cmd"]);
    expect(filterScripts(list, { situation: "problem" }).map((s) => s.id)).toEqual(["roto"]);
    expect(filterScripts(list, { situation: "noManifest" }).map((s) => s.id)).toEqual(["suelto.cmd"]);
  });
});

describe("sortScripts", () => {
  it("por nombre", () => {
    expect(sortScripts(list, "name").map((s) => s.id)).toEqual(["bancocam", "luces", "roto", "suelto.cmd"]);
  });

  it("por última corrida, la más reciente primero y las que nunca corrieron al final", () => {
    expect(sortScripts(list, "lastRun").map((s) => s.id)).toEqual(["luces", "bancocam", "roto", "suelto.cmd"]);
  });
});

describe("countText", () => {
  it("cuenta los scripts, los en uso, los con problema y los sin ficha", () => {
    expect(countText(list)).toBe("4 scripts · 2 en uso · 1 con problema · 1 sin ficha");
  });

  it("omite las partes que valen cero", () => {
    expect(countText([list[0]])).toBe("1 script · 1 en uso");
    expect(countText([])).toBe("0 scripts");
  });
});

describe("lastRunText", () => {
  const now = new Date("2026-10-27T12:00:00Z");
  it("dice hace cuánto, cómo terminó y cuánto tardó", () => {
    expect(lastRunText(list[0].lastRun, now)).toBe("hace 2 h · bien · 9 s");
    expect(lastRunText(list[1].lastRun, now)).toBe("hace 1 h · falló");
    expect(lastRunText(undefined, now)).toBe("nunca");
  });
});
