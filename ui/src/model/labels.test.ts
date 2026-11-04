import { describe, expect, it } from "vitest";
import cases from "./labels.cases.json";
import { fitsScreen, screenText, shortLabelFor } from "./labels";

describe("shortLabelFor", () => {
  it("una palabra corta queda sola, en mayúsculas y sin acentos", () => {
    expect(shortLabelFor("Short")).toEqual(["SHORT"]);
    expect(shortLabelFor("Cámara")).toEqual(["CAMARA"]);
    expect(shortLabelFor("Año")).toEqual(["ANO"]);
  });

  it("una palabra de más de seis letras se corta a cinco y un punto (04 §9: PANTA.)", () => {
    expect(shortLabelFor("Pantalla")).toEqual(["PANTA."]);
    expect(shortLabelFor("Micrófono")).toEqual(["MICRO."]);
  });

  it("junta palabras en una línea mientras entren en seis, y después pasa a la segunda", () => {
    expect(shortLabelFor("Ir al banco")).toEqual(["IR AL", "BANCO"]);
    expect(shortLabelFor("Ya arrancamos")).toEqual(["YA", "ARRAN."]);
    expect(shortLabelFor("Pantalla + Cámara")).toEqual(["PANTA.", "+"]);
  });

  it("lo que va entre paréntesis va a la segunda línea (04 §9)", () => {
    expect(shortLabelFor("Cámara (zoom)")).toEqual(["CAMARA", "ZOOM"]);
    expect(shortLabelFor("Vertical (SolidWorks)")).toEqual(["VERTI.", "SOLID."]);
  });

  it("nunca pasa de dos líneas ni de seis letras por línea, y con nada da una línea vacía", () => {
    expect(shortLabelFor("Una etiqueta muy larga de verdad")).toEqual(["UNA", "ETIQU."]);
    expect(shortLabelFor("   ")).toEqual([""]);
    expect(fitsScreen(["SHORT"])).toBe(true);
    expect(fitsScreen(["DEMASIADO"])).toBe(false);
    expect(fitsScreen(["A", "B", "C"])).toBe(false);
  });
});

// Los mismos casos los corre ShortLabelsTests del lado de C#: la consola recibe la regla de C# y Teclas muestra la de acá (plan de la 9a, decisión 12).
describe("los casos compartidos con C#", () => {
  it.each(cases.map((c) => [c.label, c.lines] as const))("%j da %j", (label, lines) => {
    expect(shortLabelFor(label)).toEqual(lines);
  });
});

describe("screenText", () => {
  it("pasa a mayúsculas sin acentos y deja los espacios, para escribir las líneas a mano", () => {
    expect(screenText("ñandú ")).toBe("NANDU ");
    expect(screenText("mic: on")).toBe("MIC ON");
  });
});
