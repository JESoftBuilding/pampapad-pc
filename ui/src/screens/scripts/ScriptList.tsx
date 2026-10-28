import type { ScriptView } from "@/bridge/contract.scripts";
import { lastRunText, type Situation } from "@/model/scripts";
import { Badge } from "@/ui/badge";
import { Input } from "@/ui/input";
import { Select } from "@/ui/select";
import { cn } from "@/ui/utils";
import type { ScriptsModel } from "./useScriptsModel";

export const statusText = { ready: "Listo", running: "Corriendo", problem: "Problema", noManifest: "Sin ficha" } as const;
export const statusTone = { ready: "ok", running: "brand", problem: "bad", noManifest: "neutral" } as const;

const situations: { value: Situation; label: string }[] = [
  { value: "all", label: "Todos" },
  { value: "used", label: "En uso" },
  { value: "unused", label: "Sin usar" },
  { value: "problem", label: "Con problema" },
  { value: "noManifest", label: "Sin ficha" },
];

/** El buscador y los filtros (05 §4.3): filtran en la página, sin pedir nada. */
export function ScriptFilters({ model }: { model: ScriptsModel }) {
  const { filter } = model;
  return (
    <div className="grid gap-2 sm:grid-cols-[1fr_10rem_10rem_10rem]">
      <Input aria-label="Buscar scripts" placeholder="Buscar por nombre, id, descripción o acción" value={filter.text} onChange={(event) => filter.setText(event.target.value)} />
      <Select aria-label="Etiqueta" value={filter.tag} onChange={(event) => filter.setTag(event.target.value)}>
        <option value="">Todas las etiquetas</option>
        {model.tags.map((tag) => (
          <option key={tag} value={tag}>
            {tag}
          </option>
        ))}
      </Select>
      <Select aria-label="Situación" value={filter.situation} onChange={(event) => filter.setSituation(event.target.value as Situation)}>
        {situations.map((situation) => (
          <option key={situation.value} value={situation.value}>
            {situation.label}
          </option>
        ))}
      </Select>
      <Select aria-label="Orden" value={filter.order} onChange={(event) => filter.setOrder(event.target.value === "lastRun" ? "lastRun" : "name")}>
        <option value="name">Por nombre</option>
        <option value="lastRun">Por última corrida</option>
      </Select>
    </div>
  );
}

/** Una fila por script: estado, nombre con el id debajo, descripción, entrada, tope y cómo terminó la última corrida. Sin ficha, en gris. */
export function ScriptList({ scripts, selected, locked = false, onSelect }: { scripts: ScriptView[]; selected?: string; locked?: boolean; onSelect: (script: ScriptView) => void }) {
  const now = new Date();
  return (
    <ul aria-label="Scripts" className="space-y-1">
      {scripts.map((script) => (
        <li key={script.id}>
          <button
            type="button"
            aria-pressed={script.id === selected}
            disabled={locked && script.id !== selected}
            onClick={() => onSelect(script)}
            className={cn(
              "w-full rounded-md border-l-2 px-2 py-1.5 text-left text-sm outline-none focus-visible:ring-2 focus-visible:ring-accent/60",
              script.id === selected ? "border-accent bg-background" : "border-transparent hover:bg-background",
              script.status === "noManifest" && "text-muted",
            )}
          >
            <span className="flex items-center gap-2">
              <span className="mr-auto">{script.name}</span>
              <Badge tone={statusTone[script.status]}>{statusText[script.status]}</Badge>
            </span>
            <span className="block font-mono text-xs text-muted">{script.id}</span>
            {script.description !== undefined && <span className="block text-xs">{script.description}</span>}
            <span className="block text-xs text-muted">
              {script.entry ?? "sin entrada"} · {script.timeoutSec} s · {lastRunText(script.lastRun, now)}
            </span>
          </button>
        </li>
      ))}
    </ul>
  );
}
