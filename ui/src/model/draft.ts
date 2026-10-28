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

/**
 * Callar el sonido con una toma en curso (console.muteSoundWhileRecording, 02 §4.10). Verdadero es el valor por defecto y no se escribe:
 * se quita el campo y, si el bloque console queda vacío, el bloque. Lo desconocido del bloque se conserva.
 */
export function setMuteSound<C extends ConfigLike & { console?: Record<string, unknown> }>(config: C, mute: boolean): C {
  const block: Record<string, unknown> = { ...(config.console ?? {}) };
  if (mute) {
    delete block.muteSoundWhileRecording;
  } else {
    block.muteSoundWhileRecording = false;
  }

  const next: C = { ...config, console: block };
  if (Object.keys(block).length === 0) {
    delete next.console;
  }

  return next;
}
