import type { ActionFinished, State } from "@/bridge/contract";
import { failureText, requestedByText } from "@/model/status";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import { Card, CardTitle } from "@/ui/card";

/** Acciones en curso (05 §4.4): cada una con quién la pidió y Cancelar, Cancelar todo, y las que terminaron con la ventana abierta. */
export function Running({ running, finished, onCancel }: { running: State["running"]; finished: ActionFinished[]; onCancel: (runId?: string) => void }) {
  return (
    <Card>
      <div className="mb-3 flex items-center justify-between">
        <CardTitle className="mb-0">Acciones en curso</CardTitle>
        <Button size="sm" variant="outline" disabled={running.length === 0} onClick={() => onCancel()}>
          Cancelar todo
        </Button>
      </div>
      {running.length === 0 ? (
        <p className="text-sm text-muted">Nada en curso.</p>
      ) : (
        <ul className="space-y-2">
          {running.map((run) => (
            <li key={run.runId} className="flex items-center justify-between gap-2 text-sm">
              <span>
                <span className="font-mono">{run.actionId}</span> <span className="text-muted">pedida por {requestedByText(run.requestedBy)}</span>
              </span>
              <Button size="sm" variant="danger" onClick={() => onCancel(run.runId)}>
                Cancelar
              </Button>
            </li>
          ))}
        </ul>
      )}
      <h3 className="mb-2 mt-4 text-xs uppercase tracking-wide text-muted">Terminaron con la ventana abierta</h3>
      {finished.length === 0 ? (
        <p className="text-sm text-muted">Ninguna todavía.</p>
      ) : (
        <ul className="space-y-1 text-sm">
          {[...finished].reverse().map((result) => (
            <li key={result.runId}>
              <Badge tone={result.ok ? "ok" : "bad"}>{result.ok ? "bien" : "falló"}</Badge> <span className="font-mono">{result.actionId}</span>{" "}
              <span className="text-muted">
                {result.ok ? `${result.durationMs} ms` : failureText(result.stepNumber, result.stepType, result.failure?.message)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
