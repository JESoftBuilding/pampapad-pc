import type { Problem, State } from "@/bridge/contract";
import { libraryText } from "@/model/status";
import { Badge } from "@/ui/badge";
import { Card, CardTitle } from "@/ui/card";

/** Problemas de la configuración (05 §4.4): cada uno con su ubicación. Llevar a la tecla o a la acción llega con esas pantallas, en la etapa 7. */
export function Problems({ problems, library }: { problems: Problem[]; library: State["library"] }) {
  return (
    <Card>
      <CardTitle>Problemas de la configuración</CardTitle>
      <p className="mb-3 text-sm text-muted">{libraryText(library.status, library.folder)}</p>
      {problems.length === 0 ? (
        <p className="text-sm">Sin problemas.</p>
      ) : (
        <ul className="space-y-2">
          {problems.map((problem) => (
            <li key={`${problem.where}|${problem.code}`} className="text-sm">
              <Badge tone={problem.severity === "error" ? "bad" : "warn"}>{problem.severity === "error" ? "error" : "aviso"}</Badge>{" "}
              <span className="font-mono text-xs text-muted">{problem.where === "" ? "config.json" : problem.where}</span>
              <p>{problem.message}</p>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
