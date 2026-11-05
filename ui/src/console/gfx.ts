import { FIRST_CHAR, FONT_6X8 } from "./font6x8";

// El pintor de la consola, en la página (plan de la 9b, decisión 14): lo que hacen GfxCanvas (firmware src/consola/Screen.cpp:41-84) y
// Adafruit GFX 1.12.6 (Adafruit_GFX.cpp: drawLine, writeLine, drawRect, drawRoundRect, fillRoundRect, fillCircle, los dos ayudantes del
// círculo y drawChar) sobre un GFXcanvas16, copiado paso por paso para que salgan los mismos píxeles. Sin rotación ni fondo de letra.

export const WIDTH = 320;
export const HEIGHT = 240;

export type Rect = { x: number; y: number; w: number; h: number };

/** 320 × 240 píxeles en RGB565, como la memoria de la pantalla. Lo que cae fuera del recorte no se pinta, como en el lienzo de una zona. */
export class Gfx {
  readonly pixels = new Uint16Array(WIDTH * HEIGHT);
  private clip: Rect = { x: 0, y: 0, w: WIDTH, h: HEIGHT };

  /** El lienzo de una zona: el firmware vuelca sólo el rectángulo de la zona (Screen.cpp, drawZone). */
  clipTo(rect: Rect) {
    this.clip = rect;
  }

  at(x: number, y: number): number {
    return this.pixels[y * WIDTH + x];
  }

  pixel(x: number, y: number, c: number) {
    const k = this.clip;
    if (x >= k.x && x < k.x + k.w && y >= k.y && y < k.y + k.h) {
      this.pixels[y * WIDTH + x] = c;
    }
  }

  // GFXcanvas16::drawFastVLine y drawFastHLine: un largo negativo va hacia atrás.
  vline(x: number, y: number, h: number, c: number) {
    if (h < 0) {
      h = -h;
      y -= h - 1;
    }
    for (let i = 0; i < h; i++) this.pixel(x, y + i, c);
  }

  hline(x: number, y: number, w: number, c: number) {
    if (w < 0) {
      w = -w;
      x -= w - 1;
    }
    for (let i = 0; i < w; i++) this.pixel(x + i, y, c);
  }

  // Adafruit_GFX::fillRect: columna por columna; un ancho de 0 o negativo no pinta.
  fillRect(x: number, y: number, w: number, h: number, c: number) {
    for (let i = x; i < x + w; i++) this.vline(i, y, h, c);
  }

  drawRect(x: number, y: number, w: number, h: number, c: number) {
    this.hline(x, y, w, c);
    this.hline(x, y + h - 1, w, c);
    this.vline(x, y, h, c);
    this.vline(x + w - 1, y, h, c);
  }

  drawRoundRect(x: number, y: number, w: number, h: number, r: number, c: number) {
    r = Math.min(r, Math.trunc(Math.min(w, h) / 2));
    this.hline(x + r, y, w - 2 * r, c);
    this.hline(x + r, y + h - 1, w - 2 * r, c);
    this.vline(x, y + r, h - 2 * r, c);
    this.vline(x + w - 1, y + r, h - 2 * r, c);
    this.circleHelper(x + r, y + r, r, 1, c);
    this.circleHelper(x + w - r - 1, y + r, r, 2, c);
    this.circleHelper(x + w - r - 1, y + h - r - 1, r, 4, c);
    this.circleHelper(x + r, y + h - r - 1, r, 8, c);
  }

  fillRoundRect(x: number, y: number, w: number, h: number, r: number, c: number) {
    r = Math.min(r, Math.trunc(Math.min(w, h) / 2));
    this.fillRect(x + r, y, w - 2 * r, h, c);
    this.fillCircleHelper(x + w - r - 1, y + r, r, 1, h - 2 * r - 1, c);
    this.fillCircleHelper(x + r, y + r, r, 2, h - 2 * r - 1, c);
  }

  fillCircle(x0: number, y0: number, r: number, c: number) {
    this.vline(x0, y0 - r, 2 * r + 1, c);
    this.fillCircleHelper(x0, y0, r, 3, 0, c);
  }

  // GfxCanvas::strokeRoundRect: el grosor crece hacia adentro, un trazo de 1 por vuelta, con el radio achicándose.
  strokeRoundRect(x: number, y: number, w: number, h: number, r: number, thick: number, c: number) {
    for (let i = 0; i < thick; i++) {
      const wi = w - 2 * i;
      const hi = h - 2 * i;
      if (wi <= 0 || hi <= 0) return;
      if (r > i) this.drawRoundRect(x + i, y + i, wi, hi, r - i, c);
      else this.drawRect(x + i, y + i, wi, hi, c);
    }
  }

  // GfxCanvas::line: líneas paralelas de 1, corridas a lo ancho de la línea.
  line(x0: number, y0: number, x1: number, y1: number, thick: number, c: number) {
    const steep = Math.abs(y1 - y0) > Math.abs(x1 - x0);
    for (let i = 0; i < thick; i++) {
      const d = i - Math.trunc(thick / 2);
      if (steep) this.drawLine(x0 + d, y0, x1 + d, y1, c);
      else this.drawLine(x0, y0 + d, x1, y1 + d, c);
    }
  }

  // print() de un texto ya saneado, sin fondo y sin salto (setTextWrap(false)): 6 × size por letra.
  text(x: number, y: number, size: number, c: number, s: string) {
    for (let n = 0; n < s.length; n++) this.char(x + n * 6 * size, y, s.charCodeAt(n), size, c);
  }

  private char(x: number, y: number, code: number, size: number, c: number) {
    const at = (code - FIRST_CHAR) * 5;
    for (let i = 0; i < 5; i++) {
      let column = FONT_6X8[at + i] ?? 0;
      for (let j = 0; j < 8; j++, column >>= 1) {
        if (column & 1) this.fillRect(x + i * size, y + j * size, size, size, c);
      }
    }
  }

  private drawLine(x0: number, y0: number, x1: number, y1: number, c: number) {
    if (x0 === x1) {
      if (y0 > y1) [y0, y1] = [y1, y0];
      this.vline(x0, y0, y1 - y0 + 1, c);
    } else if (y0 === y1) {
      if (x0 > x1) [x0, x1] = [x1, x0];
      this.hline(x0, y0, x1 - x0 + 1, c);
    } else {
      this.writeLine(x0, y0, x1, y1, c);
    }
  }

  // Bresenham de Adafruit_GFX::writeLine, con los mismos cambios de extremo: otro orden daría otros píxeles.
  private writeLine(x0: number, y0: number, x1: number, y1: number, c: number) {
    const steep = Math.abs(y1 - y0) > Math.abs(x1 - x0);
    if (steep) {
      [x0, y0] = [y0, x0];
      [x1, y1] = [y1, x1];
    }
    if (x0 > x1) {
      [x0, x1] = [x1, x0];
      [y0, y1] = [y1, y0];
    }
    const dx = x1 - x0;
    const dy = Math.abs(y1 - y0);
    let err = Math.trunc(dx / 2);
    const ystep = y0 < y1 ? 1 : -1;
    for (; x0 <= x1; x0++) {
      if (steep) this.pixel(y0, x0, c);
      else this.pixel(x0, y0, c);
      err -= dy;
      if (err < 0) {
        y0 += ystep;
        err += dx;
      }
    }
  }

  private circleHelper(x0: number, y0: number, r: number, corner: number, c: number) {
    let f = 1 - r;
    let ddFx = 1;
    let ddFy = -2 * r;
    let x = 0;
    let y = r;
    while (x < y) {
      if (f >= 0) {
        y--;
        ddFy += 2;
        f += ddFy;
      }
      x++;
      ddFx += 2;
      f += ddFx;
      if (corner & 0x4) {
        this.pixel(x0 + x, y0 + y, c);
        this.pixel(x0 + y, y0 + x, c);
      }
      if (corner & 0x2) {
        this.pixel(x0 + x, y0 - y, c);
        this.pixel(x0 + y, y0 - x, c);
      }
      if (corner & 0x8) {
        this.pixel(x0 - y, y0 + x, c);
        this.pixel(x0 - x, y0 + y, c);
      }
      if (corner & 0x1) {
        this.pixel(x0 - y, y0 - x, c);
        this.pixel(x0 - x, y0 - y, c);
      }
    }
  }

  private fillCircleHelper(x0: number, y0: number, r: number, corners: number, delta: number, c: number) {
    let f = 1 - r;
    let ddFx = 1;
    let ddFy = -2 * r;
    let x = 0;
    let y = r;
    let px = x;
    let py = y;
    delta++;
    while (x < y) {
      if (f >= 0) {
        y--;
        ddFy += 2;
        f += ddFy;
      }
      x++;
      ddFx += 2;
      f += ddFx;
      if (x < y + 1) {
        if (corners & 1) this.vline(x0 + x, y0 - y, 2 * y + delta, c);
        if (corners & 2) this.vline(x0 - x, y0 - y, 2 * y + delta, c);
      }
      if (y !== py) {
        if (corners & 1) this.vline(x0 + py, y0 - px, 2 * px + delta, c);
        if (corners & 2) this.vline(x0 - py, y0 - px, 2 * px + delta, c);
        py = y;
      }
      px = x;
    }
  }
}

/** De RGB565 a los cuatro bytes de un ImageData, con los bits altos repetidos abajo para que el blanco sea 255. */
export function toRgba(pixels: Uint16Array): Uint8ClampedArray<ArrayBuffer> {
  const out = new Uint8ClampedArray(pixels.length * 4);
  for (let i = 0; i < pixels.length; i++) {
    const p = pixels[i];
    const r = (p >> 11) & 0x1f;
    const g = (p >> 5) & 0x3f;
    const b = p & 0x1f;
    out.set([(r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2), 255], i * 4);
  }
  return out;
}
