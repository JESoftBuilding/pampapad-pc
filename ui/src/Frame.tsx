import type { ReactNode } from "react";
import type { SnapshotView } from "@/bridge/useSnapshot";
import { clock, consoleText, obsText } from "@/model/status";
import { Badge } from "@/ui/badge";
import { cn } from "@/ui/utils";

export type NavItem = { hash: string; title: string; stage?: number };

/** El marco común (05 §4): a la izquierda las cinco pantallas; arriba, OBS, la grabación con su tiempo y la consola. */
export function Frame({ screens, current, title, view, children }: { screens: NavItem[]; current: string; title: string; view: SnapshotView; children: ReactNode }) {
  return (
    <div className="flex h-full">
      <nav aria-label="Pantallas" className="flex w-52 shrink-0 flex-col gap-1 border-r border-border bg-card p-3">
        <p className="mb-4 px-2 font-display text-lg text-foreground">PampaPad</p>
        {screens.map((screen) => (
          <a
            key={screen.hash}
            href={`#/${screen.hash}`}
            aria-current={screen.hash === current ? "page" : undefined}
            className={cn(
              "flex items-center justify-between rounded-md border-l-2 px-2 py-1.5 text-sm outline-none focus-visible:ring-2 focus-visible:ring-accent/60",
              screen.hash === current ? "border-accent bg-background text-foreground" : "border-transparent text-muted hover:bg-background",
            )}
          >
            <span>{screen.title}</span>
            {screen.stage !== undefined && <span className="text-xs text-muted">etapa {screen.stage}</span>}
          </a>
        ))}
        {view.snapshot !== undefined && <p className="mt-auto px-2 text-xs text-muted">Versión {view.snapshot.version}</p>}
      </nav>
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex flex-wrap items-center gap-2 border-b border-border px-6 py-3">
          <h1 className="mr-auto font-display text-xl">{title}</h1>
          <StatusStrip view={view} />
        </header>
        <main className="flex-1 overflow-auto p-6">{children}</main>
      </div>
    </div>
  );
}

function StatusStrip({ view }: { view: SnapshotView }) {
  if (view.state === undefined) {
    return <Badge>{view.failure === undefined ? "Conectando con el programa…" : "Sin respuesta del programa"}</Badge>;
  }

  const { obs, console: link } = view.state;
  return (
    <div aria-label="Estado de un vistazo" className="flex flex-wrap gap-2">
      <Badge tone={obs.link === "ready" ? "ok" : "bad"}>{obsText(obs.link)}</Badge>
      {obs.link === "ready" && obs.recording === "recording" && <Badge tone="bad">Grabando {clock(obs.recordedMs)}</Badge>}
      {obs.link === "ready" && obs.recording === "paused" && <Badge tone="warn">En pausa</Badge>}
      {obs.link === "ready" && obs.streaming && <Badge tone="brand">Transmitiendo</Badge>}
      <Badge tone={link.state === "connected" ? "ok" : link.state === "waiting" ? "warn" : "neutral"}>{consoleText(link.state, link.port)}</Badge>
    </div>
  );
}
