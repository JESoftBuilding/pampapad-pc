import { useState } from "react";
import type { Action, Problem } from "@/bridge/contract";
import { usageText, type Usage } from "@/model/usage";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import { Input } from "@/ui/input";
import { cn } from "@/ui/utils";

type Props = { actions: Action[]; selected?: string; usageFor: (id: string) => Usage[]; problemsFor: (id: string) => Problem[]; onSelect: (id: string) => void; onCreate: (name: string) => void };

/** La lista de acciones (05 §4.2): nombre, cuántos pasos tiene y dónde se usa; y crear una nueva, cuyo id sale del nombre y no cambia. */
export function ActionList({ actions, selected, usageFor, problemsFor, onSelect, onCreate }: Props) {
  const [name, setName] = useState("");
  return (
    <div className="space-y-3">
      <ul aria-label="Acciones" className="space-y-1">
        {actions.map((action, index) => {
          const count = action.steps?.length ?? 0;
          const usage = usageFor(action.id ?? "");
          const broken = problemsFor(action.id ?? "").some((problem) => problem.severity === "error");
          return (
            <li key={action.id ?? index}>
              <button
                type="button"
                disabled={action.id === undefined}
                aria-pressed={action.id === selected}
                onClick={() => action.id !== undefined && onSelect(action.id)}
                className={cn(
                  "w-full rounded-md border-l-2 px-2 py-1.5 text-left text-sm outline-none focus-visible:ring-2 focus-visible:ring-accent/60",
                  action.id === selected ? "border-accent bg-background" : "border-transparent hover:bg-background",
                )}
              >
                <span className="flex items-center gap-2">
                  <span className="mr-auto">{action.name ?? action.id ?? "(sin id)"}</span>
                  {broken && <Badge tone="bad">problema</Badge>}
                  <span className="text-xs text-muted">
                    {count} {count === 1 ? "paso" : "pasos"}
                  </span>
                </span>
                <span className="block text-xs text-muted">{usage.length === 0 ? "Sin uso" : usage.map(usageText).join(" · ")}</span>
              </button>
            </li>
          );
        })}
      </ul>
      <div className="space-y-1 border-t border-border pt-3">
        <label htmlFor="new-action" className="block text-xs uppercase tracking-wide text-muted">
          Nombre de la acción nueva
        </label>
        <div className="flex gap-2">
          <Input id="new-action" value={name} onChange={(event) => setName(event.target.value)} placeholder="Ir al banco" />
          <Button
            size="sm"
            variant="outline"
            onClick={() => {
              onCreate(name);
              setName("");
            }}
            disabled={name.trim() === ""}
          >
            Crear acción
          </Button>
        </div>
      </div>
    </div>
  );
}
