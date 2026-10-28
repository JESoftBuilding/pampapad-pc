import { describe, expect, it } from "vitest";
import { mainPageOf, movePage, renamePage, setMainPage, setPageShown, type PagesConfigLike } from "./pages";

const config: PagesConfigLike = { pages: [{ id: "grabacion", name: "Grabación" }, { id: "escenas", name: "Escenas", show: false }, { id: "sala", name: "Sala" }] };

describe("pages", () => {
  it("sube y baja una página", () => {
    expect(movePage(config, 1, -1).pages?.map((p) => p.id)).toEqual(["escenas", "grabacion", "sala"]);
    expect(movePage(config, 1, 1).pages?.map((p) => p.id)).toEqual(["grabacion", "sala", "escenas"]);
  });

  it("subir la primera y bajar la última no cambian nada", () => {
    expect(movePage(config, 0, -1)).toBe(config);
    expect(movePage(config, 2, 1)).toBe(config);
  });

  it("sin mainPage, mover la primera fija la que era principal, para que no cambie sola", () => {
    const moved = movePage(config, 0, 1);
    expect(moved.mainPage).toBe("grabacion");
    expect(mainPageOf(moved)).toBe("grabacion");
  });

  it("elegir la principal deja una sola", () => {
    const chosen = setMainPage(config, "sala");
    expect(chosen.mainPage).toBe("sala");
    expect(mainPageOf(chosen)).toBe("sala");
    expect(mainPageOf(config)).toBe("grabacion");
  });

  it("mostrar quita show, el valor por defecto; ocultar lo escribe en falso", () => {
    expect(setPageShown(config, 1, true).pages?.[1]).toEqual({ id: "escenas", name: "Escenas" });
    expect(setPageShown(config, 0, false).pages?.[0]).toEqual({ id: "grabacion", name: "Grabación", show: false });
  });

  it("renombrar no cambia el id", () => {
    const renamed = renamePage(config, 2, "Taller");
    expect(renamed.pages?.[2]).toEqual({ id: "sala", name: "Taller" });
    expect(renamed.pages?.[0]).toBe(config.pages?.[0]);
  });
});
