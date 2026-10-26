// La etiqueta corta de la pantalla de la consola (04 §9, Propuesto): mayúsculas sin acentos, hasta dos líneas de seis letras, una
// palabra de más de seis se corta a cinco y un punto (PANTA.) y lo que va entre paréntesis pasa a la segunda línea. La regla vive en la
// PC y se calcula al mandar; config.json guarda label como se escribió y, si Emmanuel la escribe, shortLabel. Puro y sin importar nada.

export const lineLength = 6;
export const maxLines = 2;

function plain(text: string): string {
  return text
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toUpperCase()
    .replace(/[^A-Z0-9 +\-./]/g, "")
    .trim();
}

function abbreviate(word: string): string {
  return word.length > lineLength ? `${word.slice(0, lineLength - 1)}.` : word;
}

// Junta palabras en una línea mientras entren en seis letras con su espacio; lo que no entra va a la línea siguiente.
function pack(words: readonly string[], lines: string[]): void {
  for (const word of words.map(abbreviate)) {
    if (word.length === 0) {
      continue;
    }

    const last = lines.length - 1;
    if (last >= 0 && lines[last].length > 0 && lines[last].length + 1 + word.length <= lineLength) {
      lines[last] = `${lines[last]} ${word}`;
    } else if (lines.length < maxLines) {
      lines.push(word);
    } else {
      return;
    }
  }
}

/** Las líneas que mostraría la consola para esta etiqueta. */
export function shortLabelFor(label: string): string[] {
  const match = /^([^()]*)\(([^)]*)\)/.exec(label);
  const main = plain(match === null ? label : match[1]);
  const inner = match === null ? "" : plain(match[2]);
  const lines: string[] = [];
  pack(main.split(/\s+/), lines);
  if (inner.length > 0) {
    if (lines.length === 0) {
      lines.push("");
    }

    lines.splice(1, lines.length - 1);
    pack(inner.split(/\s+/), lines);
  }

  return lines.length === 0 ? [""] : lines;
}

/** Si estas líneas entran en la pantalla: hasta dos, de hasta seis letras (el mismo límite que short_label_invalid en el Validator). */
export function fitsScreen(lines: readonly string[]): boolean {
  return lines.length >= 1 && lines.length <= maxLines && lines.every((line) => line.length <= lineLength);
}
