import type { ReactNode } from "react";
import type { ConsoleFrame } from "@/bridge/contract.console";
import { ConsoleCanvas } from "@/console/ConsolePreview";
import { cn } from "@/ui/utils";

/** Lo que se elige en la consola dibujada además de una tecla (05 §4.1). */
export type Piece = "knob" | "module";

const pieceClass = (pressed: boolean) =>
  cn("border outline-none focus-visible:ring-2 focus-visible:ring-accent/60", pressed ? "border-accent" : "border-border hover:border-muted");

/**
 * La consola vista de arriba, como el boceto aprobado (decisión 28): la pantalla en chico arriba a la izquierda, la tecla del módulo y la
 * perilla debajo, y a la derecha la grilla de 4 × 3.
 */
export function ConsoleBody({ frame, piece, onPiece, children }: { frame?: ConsoleFrame; piece?: Piece; onPiece: (piece: Piece) => void; children: ReactNode }) {
  return (
    <div className="flex flex-wrap items-start gap-6">
      <div className="flex flex-col items-center gap-4">
        <div className="rounded-md border border-border bg-black p-1.5">
          <ConsoleCanvas frame={frame} label="Lo que muestra la pantalla de la consola, en chico" className="h-[132px] w-[176px]" />
        </div>
        <div className="flex items-center gap-6">
          <button type="button" aria-label="Tecla del módulo" aria-pressed={piece === "module"} onClick={() => onPiece("module")} className={cn("h-9 w-9 rounded-md bg-background", pieceClass(piece === "module"))} />
          <button type="button" aria-label="Perilla" aria-pressed={piece === "knob"} onClick={() => onPiece("knob")} className={cn("h-12 w-12 rounded-full bg-background", pieceClass(piece === "knob"))}>
            <span className="mx-auto mt-1 block h-3.5 w-[3px] rounded bg-muted" />
          </button>
        </div>
      </div>
      {children}
    </div>
  );
}

function Row({ name, does }: { name: string; does: string }) {
  return (
    <div className="flex justify-between gap-3 border-t border-border py-2 text-sm">
      <span className="text-muted">{name}</span>
      <span className="text-right">{does}</span>
    </div>
  );
}

/** Qué hacen la perilla y la tecla del módulo (05 §4.1). En la primera versión son informativas: no se configuran acá. */
export function PieceInfo({ piece, layerName }: { piece: Piece; layerName?: string }) {
  if (piece === "knob") {
    return (
      <div className="space-y-2">
        <h2 className="font-display text-sm uppercase tracking-wide text-muted">Perilla · todas las páginas</h2>
        <Row name="Girar" does="Cambia de página" />
        <Row name="Apretar" does="Atiende el aviso de la consola" />
        <p className="text-xs text-muted">En esta versión la perilla no se configura: hace siempre esto.</p>
      </div>
    );
  }

  return (
    <div className="space-y-2">
      <h2 className="font-display text-sm uppercase tracking-wide text-muted">Tecla del módulo · todas las páginas</h2>
      <Row name="Mantener" does={layerName === undefined ? "Nada: no tiene página asignada" : `Muestra ${layerName}`} />
      <Row name="Mantener y apretar 1 a 4" does="Va a esa página" />
      <p className="text-xs text-muted">La página que muestra se elige en Ajustes, en «Cómo se cambia de página».</p>
    </div>
  );
}
