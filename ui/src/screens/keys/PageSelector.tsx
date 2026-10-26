import type { Page } from "@/bridge/contract";
import { Badge } from "@/ui/badge";
import { cn } from "@/ui/utils";

/** Las páginas de pages en su orden, con la principal marcada (05 §4.1). Crear, renombrar y ordenar se hace en Ajustes (etapa 8). */
export function PageSelector({ pages, current, mainPage, onSelect }: { pages: Page[]; current?: string; mainPage?: string; onSelect: (id: string) => void }) {
  return (
    <nav aria-label="Páginas" className="flex flex-wrap gap-2">
      {pages.map((page, index) => (
        <button
          key={page.id ?? index}
          type="button"
          disabled={page.id === undefined}
          onClick={() => page.id !== undefined && onSelect(page.id)}
          className={cn(
            "flex items-center gap-2 rounded-md border px-3 py-1.5 text-sm outline-none focus-visible:ring-2 focus-visible:ring-accent/60",
            page.id !== undefined && page.id === current ? "border-accent text-foreground" : "border-border text-muted hover:text-foreground",
          )}
        >
          {page.name ?? page.id ?? "(sin id)"}
          {page.id !== undefined && page.id === mainPage && <Badge tone="warn">principal</Badge>}
          {page.type !== undefined && page.type !== "keys" && <Badge>{page.type}</Badge>}
        </button>
      ))}
      {pages.length === 0 && <p className="text-sm text-muted">No hay páginas en config.json.</p>}
    </nav>
  );
}
