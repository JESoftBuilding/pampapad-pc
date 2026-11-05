import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { deflateSync } from "node:zlib";
import { describe, expect, it } from "vitest";
import type { ConsoleFrame } from "@/bridge/contract.console";
import { drawScreen, zones } from "./draw";
import { HEIGHT, WIDTH, toRgba } from "./gfx";

// Huellas de píxeles de la pantalla entera (E9b-6): cualquier cambio del pintor, de la letra o de las zonas cambia la huella. Si el
// cambio es a propósito, se corre con PAMPAPAD_WRITE_PREVIEW=<carpeta> para ver los PNG, y se fijan las huellas nuevas.

const key = (role: ConsoleFrame["keys"][number]["role"], line1 = "", line2 = "", extra: Partial<ConsoleFrame["keys"][number]> = {}) => ({ role, line1, line2, hold: false, big: false, ...extra });

// Las doce teclas de la muestra: todos los roles, una letra grande y una sin tecla.
const keys: ConsoleFrame["keys"] = [
  key("scene", "CAMARA"), key("normal", "LUCES", "ON"), key("marker", "SHORT"), key("danger", "BORRAR"),
  key("caution", "OJO", "SEGURO"), key("disabled", "NADA"), key("normal", "X", "", { big: true }), key("none"),
  key("scene", "PANTALLA"), key("normal", "MIC"), key("marker", "INICIO"), key("none"),
];

function frame(screen: Partial<ConsoleFrame["screen"]>, rest: Partial<ConsoleFrame> = {}): ConsoleFrame {
  return {
    screen: { page: 0, recording: "stopped", flags: 0, recordedSec: 0, pausedSec: 0, spanKey: 0, canvas: 100, activeMask: 0, offMask: 0, alert: 0, labelsVersion: 1, flags2: 0, spanSec: 0, ...screen },
    keys,
    sceneLine: { text: "CAMARA", tone: 0 },
    smallLine: { text: "3 MARCAS", tone: 0 },
    strip: [],
    ...rest,
  };
}

const alert = { number: 2, title: { text: "MIC MUDO", tone: 2 }, line1: { text: "ESTAS GRABANDO", tone: 0 }, line2: { text: "REVISA EL MIC", tone: 0 }, hint: { text: "GIRA LA PERILLA", tone: 0 } };

const cases: Record<string, { frame: ConsoleFrame; hash: string }> = {
  grabando: { frame: frame({ recording: "recording", recordedSec: 2052, activeMask: 1 << 8 }), hash: "1a47aaf2" },
  pausa: { frame: frame({ recording: "paused", pausedSec: 130 }), hash: "7d85a600" },
  detenida: { frame: frame({ flags: 1 << 5 }), hash: "72f276c0" },
  sinObs: { frame: frame({ recording: "noObs" }, { smallLine: { text: "NO ESTA ABIERTO", tone: 0 }, sceneLine: { text: "", tone: 0 } }), hash: "2adfc3ed" },
  celdaActiva: { frame: frame({ activeMask: (1 << 0) | (1 << 3) | (1 << 10), offMask: 1 << 9 }), hash: "ae1159f8" },
  avisoGrave: { frame: frame({ recording: "recording", recordedSec: 2052, alert: 2 }, { alert }), hash: "8de1f88f" },
};

/** FNV-1a de 32 bits sobre los bytes RGBA del ImageData. */
function fnv1a(bytes: Uint8ClampedArray): string {
  let h = 0x811c9dc5;
  for (const b of bytes) h = Math.imul(h ^ b, 0x01000193) >>> 0;
  return h.toString(16).padStart(8, "0");
}

const crcTable = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});
function crc32(buf: Buffer): number {
  let c = 0xffffffff;
  for (const b of buf) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}
function chunk(type: string, data: Buffer): Buffer {
  const body = Buffer.concat([Buffer.from(type), data]);
  const head = Buffer.alloc(4);
  head.writeUInt32BE(data.length);
  const tail = Buffer.alloc(4);
  tail.writeUInt32BE(crc32(body));
  return Buffer.concat([head, body, tail]);
}
function png(rgba: Uint8ClampedArray): Buffer {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(WIDTH, 0);
  ihdr.writeUInt32BE(HEIGHT, 4);
  ihdr.set([8, 6, 0, 0, 0], 8);
  const raw = Buffer.alloc((WIDTH * 4 + 1) * HEIGHT);
  for (let y = 0; y < HEIGHT; y++) Buffer.from(rgba.buffer, y * WIDTH * 4, WIDTH * 4).copy(raw, y * (WIDTH * 4 + 1) + 1);
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

describe("las huellas de la pantalla de la consola", () => {
  const out = process.env.PAMPAPAD_WRITE_PREVIEW;
  if (out) mkdirSync(out, { recursive: true });

  for (const [name, { frame: f, hash }] of Object.entries(cases)) {
    it(`${name}: los píxeles son los de siempre`, () => {
      const rgba = toRgba(drawScreen(f).pixels);
      if (out) {
        writeFileSync(join(out, `${name}.png`), png(rgba));
        console.log(`HUELLA ${name} ${fnv1a(rgba)}`);
      }
      expect(fnv1a(rgba)).toBe(hash);
    });
  }

  it("un rol que la consola no conoce se dibuja normal, como en el firmware", () => {
    const odd = drawScreen(frame({}, { keys: [key("raro" as never, "LUZ"), ...keys.slice(1)] }));
    const normal = drawScreen(frame({}, { keys: [key("normal", "LUZ"), ...keys.slice(1)] }));
    const r = zones.cell(0);
    expect(fnv1a(toRgba(odd.pixels))).toBe(fnv1a(toRgba(normal.pixels)));
    expect(odd.at(r.x + 5, r.y + 8)).not.toBe(odd.at(r.x + 30, r.y + 24)); // hay letra y no el guion de la celda vacía
  });
});
