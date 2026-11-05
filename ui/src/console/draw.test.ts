import { describe, expect, it } from "vitest";
import type { ConsoleFrame } from "@/bridge/contract.console";
import { colors, drawScreen, screenText, zoneOps, zones } from "./draw";
import { Gfx, toRgba } from "./gfx";

const face = (role: ConsoleFrame["keys"][number]["role"], line1 = "", line2 = "") => ({ role, line1, line2, hold: false, big: false });

function frame(screen: Partial<ConsoleFrame["screen"]> = {}, keys: ConsoleFrame["keys"] = []): ConsoleFrame {
  return {
    screen: { page: 0, recording: "stopped", flags: 0, recordedSec: 0, pausedSec: 0, spanKey: 0, canvas: 100, activeMask: 0, offMask: 0, alert: 0, labelsVersion: 1, flags2: 0, spanSec: 0, ...screen },
    keys: [...keys, ...Array.from({ length: 12 - keys.length }, () => face("none"))],
    sceneLine: { text: "CAMARA", tone: 0 },
    smallLine: { text: "", tone: 0 },
    strip: [],
  };
}

describe("la pantalla de la consola, como la dibuja el firmware", () => {
  it("grabando, la banda es roja y el cronómetro es el tiempo del frame", () => {
    const gfx = drawScreen(frame({ recording: "recording", recordedSec: 2052 }));

    expect(gfx.at(2, 60)).toBe(colors.red);
    expect(colors.red).toBe(0xd0e7); // rgb565(0xD21F3C), la cuenta de DrawList.h
    expect(zoneOps(frame({ recording: "recording", recordedSec: 2052 }))[0].ops).toContainEqual({ kind: "text", x: 64, y: 8, size: 4, color: colors.light, text: "0:34:12" });
    expect(Array.from(toRgba(gfx.pixels).slice((60 * 320 + 2) * 4, (60 * 320 + 2) * 4 + 4))).toEqual([214, 28, 57, 255]);
  });

  it("lo que no es 0x20 a 0x7E sale ?, y el 0x00 corta el texto", () => {
    expect(screenText("AÑB\u0007C", 6)).toBe("A?B?C");
    expect(screenText("AB\u0000CD", 6)).toBe("AB");
    expect(screenText("CAMARAS", 6)).toBe("CAMARA");
    const cell = zoneOps(frame({}, [face("normal", "LUZ°")]))[2];
    expect(cell.ops).toContainEqual(expect.objectContaining({ kind: "text", text: "LUZ?" }));
  });

  it("la A de la letra de 6 × 8 es la de Adafruit, píxel por píxel", () => {
    const gfx = new Gfx();
    gfx.text(0, 0, 1, 1, "A");
    const rows = Array.from({ length: 8 }, (_, y) => Array.from({ length: 6 }, (_, x) => (gfx.at(x, y) === 1 ? "#" : ".")).join(""));

    expect(rows).toEqual(["..#...", ".#.#..", "#...#.", "#...#.", "#####.", "#...#.", "#...#.", "......"]);
  });

  it("los bordes gruesos crecen hacia adentro: el cuadrado de detenido y el borde de la banda no se salen", () => {
    const gfx = drawScreen(frame());

    expect(gfx.at(0, 0)).toBe(colors.grayLight); // el borde de 1 de la banda, en su orilla
    expect(gfx.at(1, 1)).toBe(colors.bandStopped);
    expect(gfx.at(12, 12)).toBe(colors.grayLight); // el cuadrado de 40 con borde de 3, desde (12, 12)
    expect(gfx.at(14, 14)).toBe(colors.grayLight);
    expect(gfx.at(15, 15)).toBe(colors.bandStopped);
    expect(gfx.at(11, 11)).toBe(colors.bandStopped);
    expect(gfx.at(51, 51)).toBe(colors.grayLight);
    expect(gfx.at(52, 52)).toBe(colors.bandStopped);
  });

  it("la tecla activa se invierte: el mosaico redondeado se llena y la letra queda oscura", () => {
    const keys = [face("scene", "HOLA")];
    const active = drawScreen(frame({ activeMask: 1 }, keys));
    const idle = drawScreen(frame({}, keys));
    const r = zones.cell(0);

    expect(active.at(r.x + 40, r.y + 45)).toBe(colors.light); // dentro del mosaico, fuera de la letra
    expect(active.at(r.x + 5, r.y + 8)).toBe(colors.dark); // la primera columna de la H
    expect(active.at(r.x + 2, r.y + 1)).toBe(colors.bg); // la esquina, redondeada con radio 6
    expect(active.at(r.x + 8, r.y + 1)).toBe(colors.light);
    expect(idle.at(r.x + 5, r.y + 8)).toBe(colors.light);
    expect(idle.at(r.x + 40, r.y + 45)).toBe(colors.bg);
    expect(idle.at(r.x + 40, r.y + 48)).toBe(colors.grayLight); // el borde de abajo del mosaico hueco
  });

  it("una celda sin tecla es un guion gris de 20 × 3, y un aviso tapa la línea y la grilla con sus franjas", () => {
    const gfx = drawScreen(frame());
    const r = zones.cell(5);
    expect(gfx.at(r.x + 30, r.y + 24)).toBe(colors.grayDim);
    expect(gfx.at(r.x + 50, r.y + 24)).toBe(colors.bg);

    const alerted = { ...frame({ alert: 2 }), alert: { number: 2, title: { text: "MIC MUDO", tone: 2 }, line1: { text: "ESTAS GRABANDO", tone: 0 }, line2: { text: "", tone: 0 }, hint: { text: "", tone: 0 } } };
    const ops = zoneOps(alerted);
    expect(ops).toHaveLength(5);
    expect(drawScreen(alerted).at(100, 120)).toBe(colors.red);
    expect(drawScreen(alerted).at(0, 64)).toBe(colors.light); // el marco de 6, en el color de la letra
  });
});
