import { describe, expect, it } from "vitest";
import { problemsUnder, summary, valueText, withOnlyIf, withValue } from "./steps";

const types = [
  { id: "obs.scene", name: "Poner una escena", group: "OBS", fields: [{ name: "scene", label: "Escena", kind: "obsScene", optional: false }] },
  { id: "wait.window", name: "Esperar una ventana", group: "Esperas", fields: [
    { name: "title", label: "Título", kind: "window", optional: false },
    { name: "width", label: "Ancho", kind: "number", optional: true },
    { name: "timeoutSec", label: "Tope", kind: "number", optional: false },
  ] },
];

describe("summary", () => {
  it("nombra el tipo y los campos que tienen valor, en el orden de sus Field", () => {
    expect(summary({ type: "wait.window", timeoutSec: 5, title: "bancocam" }, types)).toBe("Esperar una ventana: bancocam, 5");
    expect(summary({ type: "obs.scene", scene: { uuid: "x", name: "Banco" } }, types)).toBe("Poner una escena: Banco");
    expect(summary({ type: "obs.scene", scene: "Banco" }, types)).toBe("Poner una escena: Banco");
  });

  it("un tipo que el catálogo no tiene se dice tal cual, y sin tipo lo dice", () => {
    expect(summary({ type: "lights", command: "power" }, types)).toBe("lights (tipo desconocido)");
    expect(summary({}, types)).toBe("(sin tipo)");
  });

  it("la condición pegada va al final, negada si corresponde", () => {
    expect(summary({ type: "obs.scene", scene: "Banco", onlyIf: { type: "recording.is", state: "stopped" } }, types)).toBe("Poner una escena: Banco · sólo si recording.is");
    expect(summary({ type: "obs.scene", scene: "Banco", onlyIf: { type: "window.exists", title: "x", negate: true } }, types)).toBe("Poner una escena: Banco · sólo si no window.exists");
  });
});

describe("valueText y problemsUnder", () => {
  it("muestra un ObsRef por su nombre, un booleano en castellano y un objeto como JSON", () => {
    expect(valueText({ uuid: "x", name: "Banco" })).toBe("Banco");
    expect(valueText(true)).toBe("sí");
    expect(valueText({ a: 1 })).toBe('{"a":1}');
    expect(valueText(undefined)).toBe("");
  });

  it("withValue pone o quita un parámetro sin tocar los demás, y withOnlyIf la condición", () => {
    const step = { type: "wait.window", title: "bancocam", inventado: 1 };

    expect(withValue(step, "timeoutSec", 5)).toEqual({ type: "wait.window", title: "bancocam", inventado: 1, timeoutSec: 5 });
    expect(withValue(step, "title", undefined)).toEqual({ type: "wait.window", inventado: 1 });
    expect(withValue(step, "title", "")).toEqual({ type: "wait.window", inventado: 1 });
    expect(withOnlyIf(step, { type: "recording.is", state: "stopped" })).toEqual({ ...step, onlyIf: { type: "recording.is", state: "stopped" } });
    expect(withOnlyIf(withOnlyIf(step, { type: "recording.is" }), undefined)).toEqual(step);
  });

  it("filtra los problemas de una ubicación y de lo que cuelga de ella", () => {
    const problems = [
      { where: "pages[0].keys[1].hotkey", code: "a", message: "m", severity: "error" as const },
      { where: "pages[0].keys[10].action", code: "b", message: "m", severity: "error" as const },
      { where: "actions[0].steps[1].target", code: "c", message: "m", severity: "warning" as const },
    ];

    expect(problemsUnder(problems, "pages[0].keys[1]").map((problem) => problem.code)).toEqual(["a"]);
    expect(problemsUnder(problems, "actions[0]").map((problem) => problem.code)).toEqual(["c"]);
  });
});
