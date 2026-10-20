// El filtro del registro y cómo se juntan las líneas leídas con las que llegan en vivo (05 §4.4). Puro (01 §1).

export type Level = "info" | "warning" | "error";
export type LogFilter = { level: Level; requestedBy?: string; runId?: string };
export type LogLine = { level: Level; requestedBy?: string; runId?: string };

const rank: Record<Level, number> = { info: 0, warning: 1, error: 2 };

/** El mismo criterio que LogQuery en el núcleo: el nivel es un mínimo; requestedBy y runId, exactos. */
export function matches(line: LogLine, filter: LogFilter): boolean {
  return (
    rank[line.level] >= rank[filter.level] &&
    (filter.requestedBy === undefined || line.requestedBy === filter.requestedBy) &&
    (filter.runId === undefined || line.runId === filter.runId)
  );
}

/** Suma al final y se queda con las últimas: el registro guarda 500 en memoria (05 §9). */
export function keepLast<T>(lines: readonly T[], incoming: T, limit = 500): T[] {
  const next = [...lines, incoming];
  return next.length > limit ? next.slice(next.length - limit) : next;
}

/** Junta lo que trajo log.read con lo que llegó en vivo mientras tanto, sin repetir: de lo vivo, sólo lo posterior a lo leído. */
export function mergeLog<T extends { at: string }>(read: readonly T[], live: readonly T[]): T[] {
  const last = read.at(-1);
  if (last === undefined) {
    return [...live];
  }

  const after = Date.parse(last.at);
  return [...read, ...live.filter((entry) => Date.parse(entry.at) > after)];
}
