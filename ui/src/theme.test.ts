import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

// El fondo de la página (--color-background) y el DefaultBackgroundColor de la ventana (EditorWindow.cs) tienen que ser el mismo
// color, para que no haya destello al mostrarla (01 §9). Hasta acá lo decía un comentario en cada archivo; ahora lo mira esta prueba.
function read(relative: string): string {
  return readFileSync(fileURLToPath(new URL(relative, import.meta.url)), "utf8");
}

describe("el tema", () => {
  it("el fondo de la página es el fondo de la ventana nativa", () => {
    const css = /--color-background:\s*#([0-9a-fA-F]{6})/.exec(read("./index.css"));
    const window = /Color\.FromArgb\(0x([0-9A-Fa-f]{2}), 0x([0-9A-Fa-f]{2}), 0x([0-9A-Fa-f]{2})\)/.exec(read("../../src/PampaPad.App/EditorWindow.cs"));

    expect(css).not.toBeNull();
    expect(window).not.toBeNull();
    expect(css![1].toLowerCase()).toBe(`${window![1]}${window![2]}${window![3]}`.toLowerCase());
  });

  it("el gris sobrio: el violeta ya no es el color principal (decisión 28)", () => {
    const css = read("./index.css");

    expect(css).not.toMatch(/--color-primary:\s*#6d5df6/i);
    expect(css).toMatch(/--color-accent:\s*#f5a623/i);
  });
});
