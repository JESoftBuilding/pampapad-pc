// Las operaciones sobre el borrador de config.json (01 §1: "edita un borrador y devuelve el objeto completo"). Cada una devuelve una
// copia con lo mínimo cambiado y deja el resto como el mismo objeto: así lo que la página no conoce viaja de vuelta igual (02 §3,
// regla 1) y un campo vaciado se quita en vez de escribirse null (regla 2). Puro y sin importar nada: las formas son las mínimas.

export type KeyLike = {
  position?: number;
  label?: string;
  shortLabel?: string[];
  action?: string;
  hotkey?: string;
  onHold?: { action?: string; [extra: string]: unknown };
  [extra: string]: unknown;
};
export type PageLike = { id?: string; keys?: KeyLike[]; [extra: string]: unknown };
export type StepLike = { type?: string; [extra: string]: unknown };
export type ActionLike = { id?: string; name?: string; steps?: StepLike[]; [extra: string]: unknown };
export type ConfigLike = { pages?: PageLike[]; actions?: ActionLike[]; [extra: string]: unknown };

/** Un cambio de una tecla: lo que vale undefined se quita de la tecla. */
export type KeyPatch = Record<string, unknown>;

export const idPattern = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const reserved = new Set(["next", "previous", "main"]);
const idLength = 40;

export function keyAt(page: PageLike, position: number): KeyLike | undefined {
  return page.keys?.find((key) => key.position === position);
}

function applyPatch(key: KeyLike, patch: KeyPatch): KeyLike {
  const next: KeyLike = { ...key };
  for (const [name, value] of Object.entries(patch)) {
    if (value === undefined) {
      delete next[name];
    } else {
      next[name] = value;
    }
  }

  return next;
}

/** Cambia una tecla de una página; si la posición no tenía tecla, la crea. Las teclas quedan por posición. */
export function setKey<C extends ConfigLike>(config: C, pageId: string, position: number, patch: KeyPatch): C {
  const pageIndex = config.pages?.findIndex((page) => page.id === pageId) ?? -1;
  if (pageIndex < 0 || config.pages === undefined) {
    return config;
  }

  const page = config.pages[pageIndex];
  const keys = page.keys ?? [];
  const current = keys.find((key) => key.position === position) ?? { position };
  const others = keys.filter((key) => key.position !== position);
  const edited = applyPatch(current, patch);
  const nextKeys = [...others, edited].sort((a, b) => (a.position ?? 0) - (b.position ?? 0));
  const pages = config.pages.map((candidate, index) => (index === pageIndex ? { ...page, keys: nextKeys } : candidate));
  return { ...config, pages };
}

/** Un id con el formato de 02 §4.2 a partir de un nombre: minúsculas, números y guiones, hasta 40, y nunca next, previous ni main. */
export function slug(name: string): string {
  const base = name
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, idLength)
    .replace(/-+$/g, "");
  const id = base.length === 0 ? "accion" : base;
  return reserved.has(id) ? `${id}-1` : id;
}

/** El id de una acción nueva: el slug del nombre y, si ya está tomado, con -2, -3 y así. */
export function newActionId(config: ConfigLike, name: string): string {
  const taken = new Set((config.actions ?? []).map((action) => action.id));
  const base = slug(name);
  if (!taken.has(base)) {
    return base;
  }

  for (let n = 2; ; n++) {
    const candidate = `${base.slice(0, idLength - 1 - String(n).length)}-${n}`;
    if (!taken.has(candidate)) {
      return candidate;
    }
  }
}

/** Elegir un script en Teclas deja una acción de un solo paso script.run (05 §4.1, Propuesto). */
export function scriptAction(id: string, scriptId: string, name: string): ActionLike {
  return { id, name, steps: [{ type: "script.run", script: scriptId }] };
}

export function upsertAction<C extends ConfigLike>(config: C, action: ActionLike): C {
  const actions = config.actions ?? [];
  const index = actions.findIndex((candidate) => candidate.id === action.id);
  return { ...config, actions: index < 0 ? [...actions, action] : actions.map((candidate, i) => (i === index ? action : candidate)) };
}

export function removeAction<C extends ConfigLike>(config: C, id: string): C {
  return { ...config, actions: (config.actions ?? []).filter((action) => action.id !== id) };
}

export function setStep<A extends ActionLike>(action: A, index: number, step: StepLike): A {
  return { ...action, steps: (action.steps ?? []).map((candidate, i) => (i === index ? step : candidate)) };
}

/** Mueve un paso con su condición pegada (05 §4.2: "al arrastrar un paso, la condición viaja con él"). */
export function moveStep<A extends ActionLike>(action: A, from: number, to: number): A {
  const steps = action.steps ?? [];
  if (from < 0 || to < 0 || from >= steps.length || to >= steps.length || from === to) {
    return action;
  }

  const next = [...steps];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return { ...action, steps: next };
}

export function removeStep<A extends ActionLike>(action: A, index: number): A {
  return { ...action, steps: (action.steps ?? []).filter((_, i) => i !== index) };
}

/** Si dos configuraciones escriben lo mismo. La página compara el borrador con la foto para saber si hay cambios sin guardar. */
export function sameConfig(a: unknown, b: unknown): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

type Json = Record<string, unknown>;
const isObject = (value: unknown): value is Json => typeof value === "object" && value !== null && !Array.isArray(value);

// Escribe value en la ruta, o la quita si es undefined. Un objeto que queda vacío en el camino se quita también: lo de fábrica no se escribe
// (05 §4.5) y un bloque vacío no queda en el archivo. Lo demás de cada nivel es el mismo objeto.
function setPath<C extends object>(target: C, path: readonly string[], value: unknown): C {
  const [head, ...rest] = path;
  const current = target as Json;
  const inner = rest.length === 0 ? value : setPath(isObject(current[head]) ? current[head] : {}, rest, value);
  const next: Json = { ...current };
  if (inner === undefined || (isObject(inner) && Object.keys(inner).length === 0)) {
    delete next[head];
  } else {
    next[head] = inner;
  }

  return next as C;
}

function getPath(target: unknown, path: readonly string[]): unknown {
  return path.reduce<unknown>((at, name) => (isObject(at) ? at[name] : undefined), target);
}

/** Los valores de fábrica del bloque console (02 §4.10, su único dueño): los mismos que ConsoleSettingsWire y Gestures. */
export const consoleDefaults = {
  "screen.brightness": 80,
  "screen.dimAfterSec": 60,
  "screen.dimBrightness": 15,
  "screen.offAfterSec": 300,
  sleepAfterSec: 0,
  holdMs: 1000,
  "strip.brightness": 25,
  muteSoundWhileRecording: true,
} as const;
export type ConsolePath = keyof typeof consoleDefaults;
type ConsoleValue<P extends ConsolePath> = (typeof consoleDefaults)[P] extends boolean ? boolean : number;

/** Lo que vale un ajuste de la consola en el borrador: lo escrito si es del tipo de su valor de fábrica; si no, el de fábrica. */
export function consoleValue<P extends ConsolePath>(config: ConfigLike, path: P): ConsoleValue<P> {
  const value = getPath(config.console, path.split("."));
  return (typeof value === typeof consoleDefaults[path] ? value : consoleDefaults[path]) as ConsoleValue<P>;
}

/** Escribe un ajuste de la consola; el valor de fábrica se quita en vez de escribirse, así lo no tocado no llega al archivo. */
export function setConsoleValue<C extends ConfigLike, P extends ConsolePath>(config: C, path: P, value: ConsoleValue<P>): C {
  return setPath(config, ["console", ...path.split(".")], value === consoleDefaults[path] ? undefined : value);
}

/** Los avisos con canales en console.alerts: los graves de 04 §5, en su orden, y no_pc. */
export const alertIds = ["marker_failed", "mic_muted", "long_pause", "still_recording", "obs_lost", "recording_with_failure", "no_pc"] as const;
export type AlertId = (typeof alertIds)[number];
export type AlertChannel = "light" | "sound";

// Los graves vienen con luz y sonido; no_pc, sólo con luz (04 §6).
const alertDefault = (id: AlertId, channel: AlertChannel) => !(id === "no_pc" && channel === "sound");

export function alertChannel(config: ConfigLike, id: AlertId, channel: AlertChannel): boolean {
  const value = getPath(config.console, ["alerts", id, channel]);
  return typeof value === "boolean" ? value : alertDefault(id, channel);
}

export function setAlertChannel<C extends ConfigLike>(config: C, id: AlertId, channel: AlertChannel, on: boolean): C {
  return setPath(config, ["console", "alerts", id, channel], on === alertDefault(id, channel) ? undefined : on);
}

/** La página que se ve mientras se mantiene la tecla del módulo (moduleKey.layer); null es "Sin asignar" y quita el campo. */
export function setModuleLayer<C extends ConfigLike>(config: C, pageId: string | null): C {
  return setPath(config, ["moduleKey", "layer"], pageId ?? undefined);
}

/** "Volver a los valores por defecto" (05 §4.5): borra el bloque console entero y no toca nada más. */
export function resetConsole<C extends ConfigLike>(config: C): C {
  return setPath(config, ["console"], undefined);
}

/** Callar los sonidos de la PC con una toma en curso (console.muteSoundWhileRecording, 02 §4.10). Verdadero es de fábrica y no se escribe. */
export function setMuteSound<C extends ConfigLike>(config: C, mute: boolean): C {
  return setConsoleValue(config, "muteSoundWhileRecording", mute);
}
