import type { Action, StepType } from "@/bridge/contract";
import { summary } from "@/model/steps";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import type { RunState } from "./useDraft";

export const outcomeText = { ok: "bien", failed: "falló", omitted: "salteado", notRun: "no corrió" } as const;
export const outcomeTone = { ok: "ok", failed: "bad", omitted: "warn", notRun: "neutral" } as const;

/** Probar (05 §4.1 y §4.2): corre el borrador sin guardarlo y marca en cada paso si anduvo, falló, se salteó o no llegó a correr. */
export function RunResult({ action, run, busyReason, stepTypes, onTest, onCancel }: { action?: Action; run: RunState; busyReason?: string; stepTypes: StepType[]; onTest: () => void; onCancel: () => void }) {
  const running = run.kind === "starting" || run.kind === "running";
  const steps = action?.steps ?? [];
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        <Button variant="outline" onClick={onTest} disabled={action === undefined || running || busyReason !== undefined} title={busyReason}>
          Probar
        </Button>
        {running && (
          <Button size="sm" variant="ghost" onClick={onCancel}>
            Cancelar la prueba
          </Button>
        )}
        {running && <span className="text-sm text-muted">Corriendo…</span>}
        {busyReason !== undefined && <span className="text-xs text-muted">{busyReason}</span>}
      </div>
      {run.kind === "refused" && (
        <p role="alert" className="text-sm text-danger">
          No se pudo probar: {run.failure.message}
        </p>
      )}
      {run.kind === "finished" && (
        <div className="text-sm">
          <p>
            <Badge tone={run.result.ok ? "ok" : "bad"}>{run.result.ok ? "bien" : "falló"}</Badge> en {run.result.durationMs} ms
            {run.result.failure !== undefined && <span className="text-muted">: {run.result.failure.message}</span>}
          </p>
          <ol aria-label="Resultado de la prueba" className="mt-1 space-y-1">
            {run.result.steps.map((report, index) => (
              <li key={index}>
                <Badge tone={outcomeTone[report.outcome]}>{outcomeText[report.outcome]}</Badge> {index + 1}. {steps[index] === undefined ? "" : summary(steps[index], stepTypes)}
                {report.message !== undefined && <span className="text-muted"> · {report.message}</span>}
              </li>
            ))}
          </ol>
        </div>
      )}
    </div>
  );
}
