import { describe, expect, it } from "vitest";
import { fitsScreen, shortLabelFor } from "./labels";

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
