import { describe, expect, it } from "vitest";
import { clock, consoleText, failureText, hotkeysText, libraryText, obsText, recordingText, updateText, type ObsLink } from "./status";

describe("obsText, con los textos de la bandeja", () => {
  it.each([
    ["ready", "OBS conectado"],
    ["closed", "Sin OBS: no está abierto"],
    ["no_websocket", "Sin OBS: está abierto, sin WebSocket"],
    ["auth_failed", "Sin OBS: clave rechazada"],
    ["loading", "Sin OBS: conectando"],
    ["switching", "Sin OBS: cambiando de colección"],
    ["lost", "Sin OBS: se cortó la conexión"],
    ["kicked", "Sin OBS: OBS cerró la sesión"],
    ["exiting", "Sin OBS: se está cerrando"],
  ] as Array<[ObsLink, string]>)("%s: %s", (link, text) => {
    expect(obsText(link)).toBe(text);
  });
});

describe("clock", () => {
  it("es h:mm:ss, como la bandeja", () => {
    expect(clock(754_000)).toBe("0:12:34");
  });

  it("no corta las horas en 24", () => {
    expect(clock(25 * 3_600_000)).toBe("25:00:00");
  });

  it("redondea para abajo", () => {
    expect(clock(999)).toBe("0:00:00");
  });
});

describe("recordingText", () => {
  it("grabando con OBS conectado da el tiempo", () => {
    expect(recordingText("ready", "recording", 754_000, false)).toBe("Grabando 0:12:34");
  });

  it("en pausa avisa que las marcas no entran", () => {
    expect(recordingText("ready", "paused", 754_000, false)).toBe("En pausa: las marcas no entran");
  });

  it("sin enlace dice lo último que se supo", () => {
    expect(recordingText("lost", "recording", 1_000, false)).toBe("lo último que se supo: grabando");
  });

  it("detenido y sin transmitir no dice nada", () => {
    expect(recordingText("ready", "stopped", 0, false)).toBeUndefined();
  });

  it("transmitir se suma", () => {
    expect(recordingText("ready", "recording", 1_000, true)).toBe("Grabando 0:00:01 · Transmitiendo");
  });
});

describe("consoleText", () => {
  it("sin puerto, sin consola", () => {
    expect(consoleText("off")).toBe("Sin consola: no hay puerto en local.json");
  });

  it("conectada", () => {
    expect(consoleText("connected", "COM20")).toBe("Consola COM20: conectada");
  });

  it("esperando, con el reintento", () => {
    expect(consoleText("waiting", "COM20")).toBe("Consola COM20: sin conexión, reintenta cada 2 s");
  });

  it("suelta", () => {
    expect(consoleText("released", "COM20")).toBe("Consola COM20: suelta, se toma desde la bandeja");
  });
});

describe("hotkeysText", () => {
  it("apagados en la copia de desarrollo", () => {
    expect(hotkeysText(false, 0, 0)).toBe("Apagados en esta copia");
  });

  it("todos registrados", () => {
    expect(hotkeysText(true, 5, 0)).toBe("5 registrados");
  });

  it("con rechazados", () => {
    expect(hotkeysText(true, 4, 1)).toBe("4 registrados, 1 rechazado por Windows");
  });
});

describe("libraryText", () => {
  it("cargada dice de dónde", () => {
    expect(libraryText("loaded", "D:\\Conocimiento")).toBe("Cargada de D:\\Conocimiento");
  });

  it("sin carpeta elegida, como la bandeja", () => {
    expect(libraryText("noLibrary", "")).toBe("Sin biblioteca: elegí la carpeta de datos");
  });

  it("sin la carpeta, como la bandeja", () => {
    expect(libraryText("noLibrary", "E:\\datos")).toBe("Sin biblioteca: no encuentro E:\\datos");
  });
});

describe("updateText", () => {
  it("sin novedades", () => {
    expect(updateText("idle")).toBe("Sin versión nueva a la vista");
  });

  it("lista para instalar", () => {
    expect(updateText("available", "0.3.0")).toBe("Versión 0.3.0 lista para instalar");
  });

  it("con el motivo de la última falla, que Estado tiene que mostrar (05 §8)", () => {
    expect(updateText("available", "0.3.0", "No se pudo bajar")).toBe("Versión 0.3.0 lista para instalar. No se pudo bajar");
  });
});

describe("failureText", () => {
  it("con paso y tipo", () => {
    expect(failureText(1, "obs.marker", "Estás en pausa: la marca no entró.")).toBe("paso 1 (obs.marker): Estás en pausa: la marca no entró.");
  });

  it("sin paso, sólo el mensaje", () => {
    expect(failureText(undefined, undefined, "La acción no existe.")).toBe("La acción no existe.");
  });
});
