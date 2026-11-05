import type { ConsoleFrame } from "@/bridge/contract.console";
import { Gfx, HEIGHT, WIDTH, type Rect } from "./gfx";

// La pantalla de la consola dibujada en la página (plan de la 9b, decisión 14): copia de consoleview del firmware
// (lib/consoleview/ConsoleView.h y DrawList.h) sobre lo que la consola guardaría de este ConsoleFrame (lib/consolemodel/ConsoleModel.h).
// Las mismas zonas, colores y órdenes; las órdenes las ejecuta Gfx, que copia el pintor. Los relojes y el titileo quedan quietos en el
// instante en que llega el estado: el cronómetro dice lo que trae el frame y lo que titila se ve prendido.

export const rgb565 = (rgb: number) => ((rgb >> 8) & 0xf800) | ((rgb >> 5) & 0x07e0) | ((rgb >> 3) & 0x001f);

// La paleta de 04 §2, "Colores y dibujo de la banda" (DrawList.h).
export const colors = {
  bg: rgb565(0x0e0d17),
  bandStopped: rgb565(0x16142a),
  light: rgb565(0xf2f1fa),
  grayLight: rgb565(0xb4b2c8),
  grayDim: rgb565(0x5f5d75),
  red: rgb565(0xd21f3c),
  amber: rgb565(0xf5a623),
  dark: rgb565(0x0e0d17),
  black: 0x0000,
} as const;

const MAX_TEXT = 28;

export type Op =
  | { kind: "fillRect"; x: number; y: number; w: number; h: number; color: number }
  | { kind: "fillRoundRect"; x: number; y: number; w: number; h: number; r: number; color: number }
  | { kind: "strokeRoundRect"; x: number; y: number; w: number; h: number; r: number; thick: number; color: number }
  | { kind: "fillCircle"; x: number; y: number; r: number; color: number }
  | { kind: "line"; x0: number; y0: number; x1: number; y1: number; thick: number; color: number }
  | { kind: "text"; x: number; y: number; size: number; color: number; text: string };

/** DrawList::text: corta en maxLen o en el primer 0x00 y pone ? en lugar de lo que no es 0x20 a 0x7E. Un texto vacío no es orden. */
export function screenText(s: string, maxLen: number): string {
  let out = "";
  for (const ch of s.slice(0, Math.min(maxLen, MAX_TEXT))) {
    const code = ch.charCodeAt(0);
    if (code === 0) break;
    out += code >= 0x20 && code <= 0x7e ? ch : "?";
  }
  return out;
}

class List {
  readonly ops: Op[] = [];
  fillRect = (x: number, y: number, w: number, h: number, color: number) => this.ops.push({ kind: "fillRect", x, y, w, h, color });
  fillRoundRect = (x: number, y: number, w: number, h: number, r: number, color: number) => this.ops.push({ kind: "fillRoundRect", x, y, w, h, r, color });
  strokeRoundRect = (x: number, y: number, w: number, h: number, r: number, thick: number, color: number) =>
    this.ops.push({ kind: "strokeRoundRect", x, y, w, h, r, thick, color });
  fillCircle = (x: number, y: number, r: number, color: number) => this.ops.push({ kind: "fillCircle", x, y, r, color });
  line = (x0: number, y0: number, x1: number, y1: number, thick: number, color: number) => this.ops.push({ kind: "line", x0, y0, x1, y1, thick, color });
  text(x: number, y: number, size: number, color: number, s: string, maxLen: number) {
    const text = screenText(s, maxLen);
    if (text.length > 0) this.ops.push({ kind: "text", x, y, size, color, text });
  }
}

// ConsoleModel.h: la banda que dibuja cada estado. Arrancando y deteniendo como primer estado se ven detenidos; cambiando, sin OBS.
type Band = "noObs" | "stopped" | "recording" | "paused";
const bandOf = (recording: ConsoleFrame["screen"]["recording"]): Band =>
  recording === "recording" || recording === "paused" ? recording : recording === "noObs" || recording === "switching" ? "noObs" : "stopped";

// WireMessages.h: banderas del byte 2 y roles del estilo.
const flag = { canvasVertical: 1 << 1, preflightFailed: 1 << 2, spanOpen: 1 << 4, preflightRan: 1 << 5 };
const roles = ["none", "normal", "scene", "marker", "danger", "disabled", "caution"] as const;
const TONE_AMBER = 1;
const TONE_RED = 2;

const two = (n: number) => String(n).padStart(2, "0");
const hms = (sec: number) => `${Math.trunc(sec / 3600)}:${two(Math.trunc(sec / 60) % 60)}:${two(sec % 60)}`; // formatHms
const ms = (sec: number) => `${Math.trunc(sec / 60) % 100000}:${two(sec % 60)}`; // formatMs

/** ConsoleView.h, zoneRect: banda, línea de escena, las 12 celdas (0 a 11) y las cuatro franjas del aviso (0 a 3). */
export const zones = {
  band: { x: 0, y: 0, w: 320, h: 64 },
  sceneLine: { x: 0, y: 64, w: 320, h: 24 },
  cell: (k: number): Rect => ({ x: (k % 4) * 80, y: 88 + Math.trunc(k / 4) * 51, w: 80, h: 50 }),
  stripe: (i: number): Rect => ({ x: 0, y: 64 + i * 44, w: 320, h: 44 }),
};

function canvasBox(f: ConsoleFrame, fg: number, out: List) {
  const s = f.screen;
  const vertical = (s.flags & flag.canvasVertical) !== 0;
  const w = vertical ? 27 : 52;
  const h = vertical ? 48 : 29;
  const x = 260 + Math.trunc((56 - w) / 2);
  const y = 6 + Math.trunc((52 - h) / 2);
  const pct = Math.min(s.canvas & 0x7f, 100);
  const fill = s.canvas & 0x80 ? colors.amber : colors.light;
  if (vertical) {
    const fh = Math.trunc((h * pct) / 100);
    if (fh > 0) out.fillRect(x, y + Math.trunc((h - fh) / 2), w, fh, fill);
  } else {
    const fw = Math.trunc((w * pct) / 100);
    if (fw > 0) out.fillRect(x + Math.trunc((w - fw) / 2), y, fw, h, fill);
  }
  out.strokeRoundRect(x, y, w, h, 0, 2, fg);
}

function band(f: ConsoleFrame, out: List) {
  const s = f.screen;
  const small = (fg: number) => out.text(64, 44, 2, fg, f.smallLine.text, 16);
  switch (bandOf(s.recording)) {
    case "recording":
      out.fillRect(0, 0, 320, 64, colors.red);
      out.fillCircle(32, 32, 20, colors.light);
      out.text(64, 8, 4, colors.light, hms(s.recordedSec), 8);
      small(colors.light);
      canvasBox(f, colors.light, out);
      break;
    case "paused":
      out.fillRect(0, 0, 320, 64, colors.amber);
      out.fillRect(14, 12, 14, 40, colors.dark); // las barras, en la mitad prendida del titileo
      out.fillRect(36, 12, 14, 40, colors.dark);
      out.text(64, 8, 4, colors.dark, "PAUSA", 8);
      out.text(64, 44, 2, colors.dark, `HACE ${ms(s.pausedSec)}`, 16);
      canvasBox(f, colors.dark, out);
      break;
    case "stopped": {
      out.fillRect(0, 0, 320, 64, colors.bandStopped);
      out.strokeRoundRect(0, 0, 320, 64, 0, 1, colors.grayLight);
      out.strokeRoundRect(12, 12, 40, 40, 0, 3, colors.grayLight);
      const word = s.flags & flag.preflightFailed ? "REVISAR" : s.flags & flag.preflightRan ? "LISTO" : "";
      out.text(64, 8, 4, colors.light, word, 8);
      small(colors.light);
      canvasBox(f, colors.light, out);
      break;
    }
    case "noObs":
      out.fillRect(0, 0, 320, 64, colors.black);
      for (let x = -64; x < 320; x += 12) out.line(x, 64, x + 64, 0, 2, colors.grayLight);
      out.line(12, 12, 52, 52, 3, colors.light);
      out.line(52, 12, 12, 52, 3, colors.light);
      out.text(64, 8, 4, colors.light, "SIN OBS", 8);
      small(colors.light);
      break;
  }
}

function sceneLine(f: ConsoleFrame, out: List) {
  const t = f.sceneLine;
  const bg = t.tone === TONE_AMBER ? colors.amber : t.tone === TONE_RED ? colors.red : colors.bandStopped;
  out.fillRect(0, 64, 320, 24, bg);
  out.text(10, 68, 2, t.tone === TONE_AMBER ? colors.dark : colors.light, t.text, 25);
}

// ConsoleView.h, cell: sin teclas apretadas ni barra de mantener, que son de la consola sola.
function cell(f: ConsoleFrame, key: number, out: List) {
  const r = zones.cell(key);
  out.fillRect(r.x, r.y, r.w, r.h, colors.bg);
  const face = f.keys[key] ?? { role: "none", line1: "", line2: "", hold: false, big: false };
  const role = roles.indexOf(face.role);
  const s = f.screen;
  const active = ((s.activeMask >> key) & 1) === 1;
  const off = ((s.offMask >> key) & 1) === 1;
  const mx = r.x + 2;
  const my = r.y + 1;
  if (role <= 0) {
    out.fillRect(r.x + 30, r.y + 24, 20, 3, colors.grayDim);
    return;
  }
  const accent = face.role === "danger" ? colors.red : face.role === "caution" ? colors.amber : colors.light;
  let fg: number = colors.light;
  if (face.role === "disabled" || off) {
    fg = colors.grayDim;
  } else if (active) {
    out.fillRoundRect(mx, my, 76, 48, 6, accent);
    fg = face.role === "danger" ? colors.light : colors.dark;
  } else {
    const border = face.role === "danger" ? colors.red : face.role === "marker" || face.role === "caution" ? colors.amber : colors.grayLight;
    out.strokeRoundRect(mx, my, 76, 48, 6, face.role === "danger" ? 2 : 1, border);
  }
  if (face.big) {
    out.text(r.x + 5, r.y + 13, 3, fg, face.line1, 4);
    return;
  }
  out.text(r.x + 5, r.y + 8, 2, fg, face.line1, 6);
  const span = (s.flags & flag.spanOpen) !== 0 && s.spanKey === key + 1;
  out.text(r.x + 5, r.y + 28, 2, fg, span ? ms(s.spanSec) : face.line2, 6);
}

// ConsoleView.h, alertStripe: el cuerpo en el color del tono del título, el marco de 6 en la mitad prendida y los textos que pisan la franja.
function alertStripe(f: ConsoleFrame, stripe: number, out: List) {
  const r = zones.stripe(stripe);
  const alert = f.alert?.number === f.screen.alert ? f.alert : undefined;
  const red = alert?.title.tone === TONE_RED;
  const fg = red ? colors.light : colors.dark;
  out.fillRect(r.x, r.y, r.w, r.h, red ? colors.red : colors.amber);
  out.strokeRoundRect(0, 64, 320, 176, 0, 6, fg);
  const lines = [
    { text: alert?.title.text ?? "AVISO", x: 16, y: 78, size: 6, max: 8 },
    { text: alert?.line1.text, x: 7, y: 138, size: 3, max: 17 },
    { text: alert?.line2.text, x: 7, y: 168, size: 3, max: 17 },
    { text: alert?.hint.text, x: 10, y: 214, size: 2, max: 25 },
  ];
  for (const l of lines) {
    if (l.text !== undefined && l.y < r.y + r.h && l.y + 8 * l.size > r.y) out.text(l.x, l.y, l.size, fg, l.text, l.max);
  }
}

function paint(ops: Op[], gfx: Gfx) {
  for (const op of ops) {
    if (op.kind === "fillRect") gfx.fillRect(op.x, op.y, op.w, op.h, op.color);
    else if (op.kind === "fillRoundRect") gfx.fillRoundRect(op.x, op.y, op.w, op.h, op.r, op.color);
    else if (op.kind === "strokeRoundRect") gfx.strokeRoundRect(op.x, op.y, op.w, op.h, op.r, op.thick, op.color);
    else if (op.kind === "fillCircle") gfx.fillCircle(op.x, op.y, op.r, op.color);
    else if (op.kind === "line") gfx.line(op.x0, op.y0, op.x1, op.y1, op.thick, op.color);
    else gfx.text(op.x, op.y, op.size, op.color, op.text);
  }
}

/** Las órdenes de cada zona que se ve, en el orden en que el firmware las vuelca después de pintar la pantalla con el fondo. */
export function zoneOps(f: ConsoleFrame): Array<{ rect: Rect; ops: Op[] }> {
  const zone = (rect: Rect, fill: (out: List) => void) => {
    const out = new List();
    fill(out);
    return { rect, ops: out.ops };
  };
  const result = [zone(zones.band, (out) => band(f, out))];
  // Con un aviso a la vista, las franjas tapan la línea y la grilla (ConsoleModel.h, takeZone).
  if (f.screen.alert !== 0) {
    for (let i = 0; i < 4; i++) result.push(zone(zones.stripe(i), (out) => alertStripe(f, i, out)));
  } else {
    result.push(zone(zones.sceneLine, (out) => sceneLine(f, out)));
    for (let k = 0; k < 12; k++) result.push(zone(zones.cell(k), (out) => cell(f, k, out)));
  }
  return result;
}

/** La pantalla entera de este frame, en RGB565: el fondo y después cada zona recortada a su rectángulo. */
export function drawScreen(f: ConsoleFrame): Gfx {
  const gfx = new Gfx();
  gfx.pixels.fill(colors.bg);
  for (const { rect, ops } of zoneOps(f)) {
    gfx.clipTo(rect);
    paint(ops, gfx);
  }
  gfx.clipTo({ x: 0, y: 0, w: WIDTH, h: HEIGHT });
  return gfx;
}
