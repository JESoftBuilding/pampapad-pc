import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import type { SaveState } from "./useDraft";

/** Guardar y descartar, y lo que dio el último guardado (05 §4.1): los avisos que quedaron, o la falla con cada problema y su ubicación. */
export function SaveBar({ dirty, outdated, state, onSave, onDiscard, onReload }: { dirty: boolean; outdated: boolean; state: SaveState; onSave: () => void; onDiscard: () => void; onReload: () => void }) {
  return (
    <div className="space-y-2 border-t border-border pt-3">
      {outdated && (
        <p role="alert" className="text-sm text-accent">
          La biblioteca cambió (una recarga, o un guardado desde otro lado) y este borrador es de la versión anterior: guardar va a fallar.{" "}
          <Button size="sm" variant="outline" onClick={onDiscard}>
            Descartar y tomar la nueva
          </Button>
        </p>
      )}
      <div className="flex flex-wrap items-center gap-2">
        <Button onClick={onSave} disabled={!dirty || state.kind === "saving"}>
          Guardar
        </Button>
        <Button variant="outline" onClick={onDiscard} disabled={!dirty}>
          Descartar
        </Button>
        {dirty && <Badge tone="warn">cambios sin guardar</Badge>}
        {state.kind === "saving" && <span className="text-sm text-muted">Guardando…</span>}
        {state.kind === "saved" && <span className="text-sm text-ok">Guardado.</span>}
      </div>
      {state.kind === "saved" && state.problems.length > 0 && (
        <div className="text-sm">
          <p className="text-muted">
            Se guardó con {state.problems.length} {state.problems.length === 1 ? "aviso" : "avisos"}:
          </p>
          <ProblemList problems={state.problems} />
        </div>
      )}
      {state.kind === "failed" && (
        <div role="alert" className="space-y-1 text-sm text-danger">
          <p>
            No se guardó: {state.failure.message}
            {state.failure.where !== undefined && <span className="font-mono text-xs"> ({state.failure.where})</span>}
          </p>
          {state.failure.problems !== undefined && state.failure.problems.length > 1 && <ProblemList problems={state.failure.problems} />}
          {state.failure.code === "config_changed_on_disk" && (
            <Button size="sm" variant="outline" onClick={onReload}>
              Recargar biblioteca
            </Button>
          )}
        </div>
      )}
    </div>
  );
}

function ProblemList({ problems }: { problems: ReadonlyArray<{ where: string; message: string; severity: string }> }) {
  return (
    <ul className="mt-1 space-y-1">
      {problems.map((problem) => (
        <li key={`${problem.where}|${problem.message}`}>
          <Badge tone={problem.severity === "error" ? "bad" : "warn"}>{problem.severity === "error" ? "error" : "aviso"}</Badge>{" "}
          <span className="font-mono text-xs text-muted">{problem.where === "" ? "config.json" : problem.where}</span> {problem.message}
        </li>
      ))}
    </ul>
  );
}
