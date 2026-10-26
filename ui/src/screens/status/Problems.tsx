import type { Config, Problem, State } from "@/bridge/contract";
import { libraryText } from "@/model/status";
import { linkFor } from "@/model/where";
import { Badge } from "@/ui/badge";
import { Card, CardTitle } from "@/ui/card";

/** Problemas de la configuración (05 §4.4): cada uno con su ubicación, que lleva a la tecla o a la acción; los de la lista general no tienen pantalla hasta Ajustes (etapa 8). */
export function Problems({ problems, library, config }: { problems: Problem[]; library: State["library"]; config: Config }) {
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
              <Where where={problem.where} link={linkFor(problem.where, config)} />
              <p>{problem.message}</p>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

function Where({ where, link }: { where: string; link?: string }) {
  const text = where === "" ? "config.json" : where;
  return link === undefined ? (
    <span className="font-mono text-xs text-muted">{text}</span>
  ) : (
    <a href={link} className="font-mono text-xs text-accent underline-offset-2 hover:underline">
      {text}
    </a>
  );
}
