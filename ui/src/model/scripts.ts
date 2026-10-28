// La lista de la pantalla Scripts (05 §4.3): buscar, filtrar, ordenar y contar. La lista entera viaja en scripts.list y se filtra acá,
// sin paginado. Puro y sin importar nada: las formas son las mínimas de ScriptView (contract.scripts.ts).

export type ScriptRunLike = { at: string; end: { ok: boolean; durationMs: number } };
export type ScriptLike = {
  id: string;
  name: string;
  description?: string;
  tags: string[];
  status: string;
  usedBy: { action: string; actionName?: string }[];
  lastRun?: ScriptRunLike;
};

export type Situation = "all" | "used" | "unused" | "problem" | "noManifest";
export type ScriptFilter = { text?: string; tag?: string; situation?: Situation };
export type ScriptOrder = "name" | "lastRun";

/** Sin mayúsculas ni tildes: "camara" encuentra "Cámara". */
function fold(text: string): string {
  return text.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
}

function matchesSituation(script: ScriptLike, situation: Situation): boolean {
  switch (situation) {
    case "used":
      return script.usedBy.length > 0;
    case "unused":
      return script.usedBy.length === 0;
    case "problem":
      return script.status === "problem";
    case "noManifest":
      return script.status === "noManifest";
    default:
      return true;
  }
}

/** Busca en el nombre, el id, la descripción y el nombre (o el id) de las acciones que lo usan. */
export function filterScripts<S extends ScriptLike>(scripts: S[], filter: ScriptFilter): S[] {
  const wanted = fold(filter.text?.trim() ?? "");
  return scripts.filter((script) => {
    const haystack = [script.name, script.id, script.description ?? "", ...script.usedBy.map((use) => use.actionName ?? use.action)];
    return (
      (wanted.length === 0 || haystack.some((part) => fold(part).includes(wanted))) &&
      (filter.tag === undefined || filter.tag.length === 0 || script.tags.includes(filter.tag)) &&
      matchesSituation(script, filter.situation ?? "all")
    );
  });
}

/** Por nombre, o por última corrida con la más reciente primero; las que nunca corrieron van al final, por nombre. */
export function sortScripts<S extends ScriptLike>(scripts: S[], order: ScriptOrder): S[] {
  const byName = (a: S, b: S) => a.name.localeCompare(b.name, "es");
  return [...scripts].sort((a, b) => {
    if (order === "lastRun") {
      const at = (b.lastRun ? Date.parse(b.lastRun.at) : -Infinity) - (a.lastRun ? Date.parse(a.lastRun.at) : -Infinity);
      if (at !== 0 && !Number.isNaN(at)) {
        return at;
      }
    }

    return byName(a, b);
  });
}

/** "23 scripts · 21 en uso · 1 con problema · 1 sin ficha", sin las partes que valen cero salvo el total. */
export function countText(scripts: ScriptLike[]): string {
  const used = scripts.filter((script) => script.usedBy.length > 0).length;
  const problems = scripts.filter((script) => script.status === "problem").length;
  const loose = scripts.filter((script) => script.status === "noManifest").length;
  const parts = [`${scripts.length} ${scripts.length === 1 ? "script" : "scripts"}`];
  if (used > 0) parts.push(`${used} en uso`);
  if (problems > 0) parts.push(`${problems} con problema`);
  if (loose > 0) parts.push(`${loose} sin ficha`);
  return parts.join(" · ");
}

function ago(at: string, now: Date): string {
  const minutes = Math.max(0, Math.round((now.getTime() - Date.parse(at)) / 60000));
  if (minutes < 1) return "recién";
  if (minutes < 60) return `hace ${minutes} min`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `hace ${hours} h`;
  const days = Math.round(hours / 24);
  return days === 1 ? "ayer" : `hace ${days} días`;
}

/** Cómo terminó la última corrida (05 §4.3): "hace 2 h · bien · 9 s", "ayer · falló", "nunca". */
export function lastRunText(run: ScriptRunLike | undefined, now: Date): string {
  if (run === undefined) {
    return "nunca";
  }

  const result = run.end.ok ? `bien · ${Math.round(run.end.durationMs / 1000)} s` : "falló";
  return `${ago(run.at, now)} · ${result}`;
}
