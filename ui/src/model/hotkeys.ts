// La gramática de un atajo (02 §4.3), del lado de la página: de uno a tres modificadores entre Ctrl, Shift y Alt y una tecla entre
// A a Z, 0 a 9 y F1 a F24, escrito siempre en el orden Ctrl, Shift, Alt y la tecla. Es el espejo de Hotkey.Parse y Hotkey.ToString
// (src/PampaPad.Core/Model/Hotkey.cs). Puro y sin importar nada (01 §1).

export type KeyPress = { key: string; code: string; ctrlKey: boolean; shiftKey: boolean; altKey: boolean; metaKey: boolean };

export type Capture = { kind: "hotkey"; text: string } | { kind: "pending" } | { kind: "rejected"; reason: string };

const modifierCodes = /^(Control|Shift|Alt|Meta|OS)(Left|Right)?$/;

/** La tecla de un KeyboardEvent.code, con el nombre con que se escribe: KeyB es B, Digit1 es 1, F8 es F8. null si no entra en la gramática. */
function keyOfCode(code: string): string | null {
  const letter = /^Key([A-Z])$/.exec(code);
  if (letter !== null) {
    return letter[1];
  }

  const digit = /^Digit([0-9])$/.exec(code);
  if (digit !== null) {
    return digit[1];
  }

  const fkey = /^F([1-9]|1[0-9]|2[0-4])$/.exec(code);
  return fkey === null ? null : code;
}

function write(ctrl: boolean, shift: boolean, alt: boolean, key: string): string {
  return `${ctrl ? "Ctrl+" : ""}${shift ? "Shift+" : ""}${alt ? "Alt+" : ""}${key}`;
}

/**
 * Lo que da una tecla apretada durante la captura (05 §6, punto 2). Se usa el código de la tecla y no el carácter, porque con Shift
 * un 1 escribe "!". Un modificador solo todavía no es nada: la captura sigue. La tecla Windows no se admite y una tecla sola tampoco.
 */
export function fromKeyPress(press: KeyPress): Capture {
  if (modifierCodes.test(press.code)) {
    return { kind: "pending" };
  }

  if (press.metaKey) {
    return { kind: "rejected", reason: "La tecla Windows no se admite en un atajo (02 §4.3)." };
  }

  if (!press.ctrlKey && !press.shiftKey && !press.altKey) {
    return { kind: "rejected", reason: "Hace falta por lo menos un modificador: Ctrl, Shift o Alt." };
  }

  const key = keyOfCode(press.code);
  if (key === null) {
    return { kind: "rejected", reason: `${press.key} no se admite: sólo letras, números y F1 a F24.` };
  }

  return { kind: "hotkey", text: write(press.ctrlKey, press.shiftKey, press.altKey, key) };
}

/** Lee un texto escrito a mano, sin distinguir mayúsculas, y devuelve el texto canónico, o null si no es un atajo (como Hotkey.Parse). */
export function parseHotkey(text: string): string | null {
  const parts = text.split("+").map((part) => part.trim().toUpperCase());
  if (parts.length < 2) {
    return null;
  }

  let ctrl = false;
  let shift = false;
  let alt = false;
  for (const modifier of parts.slice(0, -1)) {
    if (modifier === "CTRL" && !ctrl) {
      ctrl = true;
    } else if (modifier === "SHIFT" && !shift) {
      shift = true;
    } else if (modifier === "ALT" && !alt) {
      alt = true;
    } else {
      return null;
    }
  }

  const last = parts[parts.length - 1];
  const key = /^[A-Z0-9]$/.test(last) ? last : /^F([1-9]|1[0-9]|2[0-4])$/.test(last) ? last : null;
  return key === null ? null : write(ctrl, shift, alt, key);
}

type OwnerKey = { position?: number; hotkey?: string };
type OwnerPage = { id?: string; name?: string; keys?: OwnerKey[] };
export type HotkeyConfigLike = { pages?: OwnerPage[]; hotkeys?: { hotkey?: string }[] };

/**
 * Dónde ya se usa esta combinación (05 §6, tabla de conflictos, fila 1), con la misma regla del validador (hotkey_repeated): otra tecla
 * de la misma página ("la tecla 6 de Grabación") o un atajo general ("un atajo general"), o null. Se compara con la gramática de arriba,
 * así que "ctrl+alt+shift+p" es "Ctrl+Shift+Alt+P"; la tecla que se está editando no cuenta contra sí misma.
 */
export function usedBy(config: HotkeyConfigLike, pageId: string, position: number, text: string): string | null {
  const wanted = parseHotkey(text);
  if (wanted === null) {
    return null;
  }

  const page = config.pages?.find((candidate) => candidate.id === pageId);
  const other = page?.keys?.find((key) => key.position !== position && key.hotkey !== undefined && parseHotkey(key.hotkey) === wanted);
  if (page !== undefined && other !== undefined) {
    return `la tecla ${other.position} de ${page.name ?? page.id ?? "?"}`;
  }

  return config.hotkeys?.some((binding) => binding.hotkey !== undefined && parseHotkey(binding.hotkey) === wanted) === true ? "un atajo general" : null;
}
