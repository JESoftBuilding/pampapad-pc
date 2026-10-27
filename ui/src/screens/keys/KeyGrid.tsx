import type { Page, Problem } from "@/bridge/contract";
import { keyAt } from "@/model/draft";
import { shortLabelFor } from "@/model/labels";
import { cn } from "@/ui/utils";

const positions = Array.from({ length: 12 }, (_, index) => index + 1);

/** Las 12 teclas en 4 × 3, por filas, con su etiqueta corta (04 §9). Una tecla con problema se ve rota y las otras once siguen (decisión 28). */
export function KeyGrid({ page, selected, problemsOf, onSelect }: { page?: Page; selected?: number; problemsOf: (position: number) => Problem[]; onSelect: (position: number) => void }) {
  return (
    <div role="group" aria-label="Las doce teclas" className="grid w-fit grid-cols-4 gap-2">
      {positions.map((position) => {
        const key = page === undefined ? undefined : keyAt(page, position);
        const lines = key?.shortLabel ?? (key?.label === undefined ? [] : shortLabelFor(key.label));
        const broken = problemsOf(position).some((problem) => problem.severity === "error");
        return (
          <button
            key={position}
            type="button"
            aria-label={`Tecla ${position}`}
            aria-pressed={position === selected}
            title={key?.hotkey}
            onClick={() => onSelect(position)}
            className={cn(
              "flex h-20 w-24 flex-col items-start justify-between rounded-md border p-2 text-left outline-none focus-visible:ring-2 focus-visible:ring-accent/60",
              position === selected ? "border-accent bg-background" : "border-border bg-card hover:border-muted",
              broken && "border-danger border-dashed",
            )}
          >
            <span className="text-[10px] text-muted">{position}</span>
            <span className="font-display text-xs leading-tight text-foreground">
              {lines.length === 0 ? (
                <span className="text-muted">—</span>
              ) : (
                lines.map((line, index) => (
                  <span key={index} className="block">
                    {line}
                  </span>
                ))
              )}
            </span>
            <span className="text-[10px] text-muted">{key?.hotkey ?? ""}</span>
          </button>
        );
      })}
    </div>
  );
}
