import type { LogEntry } from "@/bridge/contract";
import type { LogFilter } from "@/model/log";
import { levelText, requestedByText, timeOfDay, triggers } from "@/model/status";
import { Button } from "@/ui/button";
import { Card, CardTitle } from "@/ui/card";
import { cn } from "@/ui/utils";

const selectClass = "rounded border border-border bg-background px-2 py-1 text-foreground";

// La clave de cada fila es un número por objeto, no la posición: la lista va invertida, así que una línea nueva correría el índice de
// todas y React volvería a montar hasta 500 filas (el teclado perdería el foco con cada línea). keepLast, mergeLog y el filtro conservan
// las referencias, de modo que la misma línea tiene siempre la misma clave.
const rowIds = new WeakMap<LogEntry, number>();
let lastRowId = 0;

function rowKey(entry: LogEntry): number {
  let id = rowIds.get(entry);
  if (id === undefined) {
    lastRowId += 1;
    id = lastRowId;
    rowIds.set(entry, id);
  }

  return id;
}

/** Registro (05 §4.4): las últimas entradas y las nuevas en vivo, con filtro por nivel, por quién la disparó y por runId. */
export function LogView({ entries, filter, onFilter }: { entries: LogEntry[]; filter: LogFilter; onFilter: (filter: LogFilter) => void }) {
  return (
    <Card className="xl:col-span-2">
      <div className="mb-3 flex flex-wrap items-center gap-3 text-sm">
        <CardTitle className="mb-0 mr-auto">Registro</CardTitle>
        <select aria-label="Nivel" className={selectClass} value={filter.level} onChange={(e) => onFilter({ ...filter, level: e.target.value as LogFilter["level"] })}>
          <option value="info">Todo</option>
          <option value="warning">Avisos y errores</option>
          <option value="error">Sólo errores</option>
        </select>
        <select
          aria-label="Quién"
          className={selectClass}
          value={filter.requestedBy ?? ""}
          onChange={(e) => onFilter({ ...filter, requestedBy: e.target.value === "" ? undefined : e.target.value })}
        >
          <option value="">Cualquiera</option>
          {triggers.map((trigger) => (
            <option key={trigger} value={trigger}>
              {requestedByText(trigger)}
            </option>
          ))}
        </select>
        {filter.runId !== undefined && (
          <Button size="sm" variant="ghost" onClick={() => onFilter({ ...filter, runId: undefined })}>
            Quitar runId {filter.runId.slice(0, 8)}
          </Button>
        )}
      </div>
      {entries.length === 0 ? (
        <p className="text-sm text-muted">Nada con este filtro.</p>
      ) : (
        <ol className="max-h-96 space-y-1 overflow-auto font-mono text-xs">
          {[...entries].reverse().map((entry) => (
            <li key={rowKey(entry)} className="grid grid-cols-[5rem_3.5rem_6rem_1fr] gap-2">
              <time dateTime={entry.at}>{timeOfDay(entry.at)}</time>
              <span className={cn(entry.level === "error" && "text-danger", entry.level === "warning" && "text-accent")}>{levelText(entry.level)}</span>
              <span className="text-muted">{entry.module}</span>
              <span>
                {entry.message}
                {entry.runId !== undefined && (
                  <button type="button" className="ml-2 text-accent underline" onClick={() => onFilter({ ...filter, runId: entry.runId })}>
                    sólo esta ejecución
                  </button>
                )}
              </span>
            </li>
          ))}
        </ol>
      )}
    </Card>
  );
}
