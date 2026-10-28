import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import type { TestState } from "./useScriptsModel";

/** La leyenda de 05 §4.3: Probar se apaga mientras se graba, se está en pausa o se transmite. */
export const noTestWhileTake = "No se prueba mientras se graba.";

/** Probar (05 §4.3): la salida en vivo línea por línea, con las de error marcadas; al final el código, la duración y el mensaje. */
export function ScriptTest({ id, test, busy, disabledReason, onTest, onCancel }: { id: string; test: TestState; busy: boolean; disabledReason?: string; onTest: () => void; onCancel: () => void }) {
  const mine = test.kind !== "idle" && test.id === id ? test : undefined;
  const running = mine?.kind === "starting" || (mine?.kind === "running" && mine.output.end === undefined);
  const reason = busy ? noTestWhileTake : disabledReason;
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        <Button variant="outline" onClick={onTest} disabled={running || reason !== undefined} title={reason}>
          Probar
        </Button>
        {running && (
          <Button size="sm" variant="ghost" onClick={onCancel}>
            Cancelar la prueba
          </Button>
        )}
        {running && <span className="text-sm text-muted">Corriendo…</span>}
        {reason !== undefined && <span className="text-xs text-muted">{reason}</span>}
      </div>
      {mine?.kind === "refused" && (
        <p role="alert" className="text-sm text-danger">
          No se pudo probar: {mine.failure.message}
        </p>
      )}
      {mine?.kind === "running" && (
        <div className="space-y-1 text-sm">
          <ol aria-label="Salida de la prueba" className="max-h-64 overflow-auto rounded-md border border-border bg-background p-2 font-mono text-xs">
            {mine.output.lines.map((line, index) => (
              <li key={index} className={line.stream === "stderr" ? "text-danger" : line.stream === "notice" ? "text-accent" : undefined} data-stream={line.stream}>
                {line.line}
              </li>
            ))}
          </ol>
          {mine.output.end !== undefined && (
            <p>
              <Badge tone={mine.output.end.ok ? "ok" : "bad"}>{mine.output.end.ok ? "bien" : "falló"}</Badge>
              {mine.output.end.exitCode !== undefined && <span> código {mine.output.end.exitCode}</span>} · {mine.output.end.durationMs} ms
              {mine.output.end.message !== undefined && <span className="text-muted"> · {mine.output.end.message}</span>}
            </p>
          )}
        </div>
      )}
    </div>
  );
}
