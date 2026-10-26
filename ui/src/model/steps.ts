// Cómo se lee un paso en la lista de Acciones: el nombre de su tipo y los valores de sus campos, en el orden de los Field (05 §4.2,
// "tipo, resumen, su condición"). El resumen no viaja desde C# (Propuesto): sale de lo que ya trae app.snapshot. Puro y sin importar nada.

export type FieldLike = { name: string; label: string; kind: string; optional: boolean; options?: string[] };
export type StepTypeLike = { id: string; name: string; fields: FieldLike[] };
export type StepLike = { type?: string; onlyIf?: { type?: string; negate?: boolean; [extra: string]: unknown } | undefined; [extra: string]: unknown };
export type ProblemLike = { where: string; code: string; message: string; severity: "error" | "warning" };

/** El texto de un valor: una referencia a OBS por su nombre, un booleano en castellano, un objeto como JSON. */
export function valueText(value: unknown): string {
  if (value === undefined || value === null) {
    return "";
  }

  if (typeof value === "boolean") {
    return value ? "sí" : "no";
  }

  if (typeof value === "object") {
    const name = (value as { name?: unknown }).name;
    return typeof name === "string" ? name : JSON.stringify(value);
  }

  return String(value);
}

export function summary(step: StepLike, types: readonly StepTypeLike[]): string {
  if (step.type === undefined || step.type.length === 0) {
    return "(sin tipo)";
  }

  const type = types.find((candidate) => candidate.id === step.type);
  const head = type === undefined ? `${step.type} (tipo desconocido)` : type.name;
  const values = (type?.fields ?? []).map((field) => valueText(step[field.name])).filter((text) => text.length > 0);
  const onlyIf = step.onlyIf === undefined || step.onlyIf.type === undefined ? "" : ` · sólo si ${step.onlyIf.negate === true ? "no " : ""}${step.onlyIf.type}`;
  return `${head}${values.length > 0 ? `: ${values.join(", ")}` : ""}${onlyIf}`;
}

/** Un parámetro del paso con otro valor; con undefined o texto vacío, se quita (02 §3, regla 2: nunca null ni vacíos). */
export function withValue<S extends StepLike>(step: S, name: string, value: unknown): S {
  const next: Record<string, unknown> = { ...step };
  if (value === undefined || value === "") {
    delete next[name];
  } else {
    next[name] = value;
  }

  return next as S;
}

export function withOnlyIf<S extends StepLike>(step: S, onlyIf: StepLike["onlyIf"]): S {
  const next: Record<string, unknown> = { ...step };
  if (onlyIf === undefined) {
    delete next.onlyIf;
  } else {
    next.onlyIf = onlyIf;
  }

  return next as S;
}

/** Los problemas de una ubicación y de lo que cuelga de ella: "pages[0].keys[1]" toma "pages[0].keys[1].hotkey" y no "pages[0].keys[10]". */
export function problemsUnder<P extends ProblemLike>(problems: readonly P[], prefix: string): P[] {
  return problems.filter((problem) => problem.where === prefix || problem.where.startsWith(`${prefix}.`));
}
