"""Arma las imágenes del instalador desde el logo del canal (los lentes con la onda cuadrada).

- src/PampaPad.App/Resources/pampapad.ico: el ícono del programa y del instalador, los lentes sobre un cuadrado oscuro.
- build/assets/instalando.gif: lo que muestra el instalador mientras instala, la misma animación del arranque de la
  consola (decisión 29 de la spec) sin su barra de carga, porque la barra la dibuja Velopack.

No corre en el CI: las imágenes van commiteadas. Hace falta Inkscape y Pillow.
Uso: py build/assets/generar.py [carpeta de los SVG del logo]
"""
import re
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw

RAIZ = Path(__file__).resolve().parents[2]
LOGO = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(r"D:\Conocimiento\Areas\marca-personal\logo\svg")
INKSCAPE = Path(r"C:\Program Files\Inkscape\bin\inkscape.com")

FONDO = (14, 13, 23)  # #0E0D17, el fondo del canal
ICONO, ICONO_LENTES, ICONO_RADIO = 256, 224, 48
GIF_ANCHO, GIF_ALTO, GIF_LOGO = 480, 360, 345
CUADROS, MS, FINAL_MS = 22, 50, 1200


def exportar(svg_texto: str, ancho: int, tmp: str, nombre: str) -> Image.Image:
    fuente = Path(tmp) / f"{nombre}.svg"
    salida = Path(tmp) / f"{nombre}.png"
    fuente.write_text(svg_texto, encoding="utf-8")
    subprocess.run([str(INKSCAPE), str(fuente), "--export-type=png", f"--export-filename={salida}",
                    f"--export-width={ancho}"], check=True, capture_output=True)
    return Image.open(salida).convert("RGBA")


def icono(tmp: str) -> None:
    lentes = exportar((LOGO / "simbolo.svg").read_text(encoding="utf-8"), ICONO_LENTES, tmp, "simbolo")
    base = Image.new("RGBA", (ICONO, ICONO), (0, 0, 0, 0))
    ImageDraw.Draw(base).rounded_rectangle([0, 0, ICONO - 1, ICONO - 1], radius=ICONO_RADIO, fill=FONDO + (255,))
    base.alpha_composite(lentes, ((ICONO - lentes.width) // 2, (ICONO - lentes.height) // 2))
    destino = RAIZ / "src" / "PampaPad.App" / "Resources" / "pampapad.ico"
    base.save(destino, format="ICO", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
    print(f"{destino.relative_to(RAIZ)}: {destino.stat().st_size} bytes")


def animacion(tmp: str) -> None:
    svg = (LOGO / "principal.svg").read_text(encoding="utf-8")
    # La onda son los dos trazos oscuros adentro de los lentes: sin ellos queda el cuadro de partida.
    sin_onda = re.sub(r'<path d="[^"]*" fill="none" stroke="#0E0D17"[^>]*/>', "", svg)
    if sin_onda == svg:
        raise SystemExit("No encontré la onda en principal.svg")
    con = exportar(svg, GIF_LOGO, tmp, "con")
    sin = exportar(sin_onda, GIF_LOGO, tmp, "sin")
    x0, y0 = (GIF_ANCHO - con.width) // 2, (GIF_ALTO - con.height) // 2
    cuadros, tiempos = [], []
    for n in range(CUADROS + 1):
        img = Image.new("RGB", (GIF_ANCHO, GIF_ALTO), FONDO)
        img.paste(sin, (x0, y0), sin)
        hasta = con.width if n == CUADROS else round(con.width * (n + 1) / CUADROS)
        tramo = con.crop((0, 0, hasta, con.height))
        img.paste(tramo, (x0, y0), tramo)
        cuadros.append(img.convert("P", palette=Image.ADAPTIVE, colors=64))
        tiempos.append(FINAL_MS if n == CUADROS else MS)
    destino = RAIZ / "build" / "assets" / "instalando.gif"
    cuadros[0].save(destino, save_all=True, append_images=cuadros[1:], duration=tiempos, loop=0, disposal=1)
    print(f"{destino.relative_to(RAIZ)}: {destino.stat().st_size} bytes, {len(cuadros)} cuadros")


if __name__ == "__main__":
    with tempfile.TemporaryDirectory() as carpeta:
        icono(carpeta)
        animacion(carpeta)
