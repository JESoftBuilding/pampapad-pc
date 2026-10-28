import type { Failure } from "@/bridge/contract";
import { Button } from "@/ui/button";

type Props = { library: boolean; local: boolean; libraryFailure?: Failure; localFailure?: Failure; saving: boolean; saved: boolean; onSave: () => void; onDiscard: () => void };

/**
 * La barra de Ajustes (05 §4.5): "Cambios sin guardar en: Biblioteca / Esta PC", con Descartar y Guardar. Guardar manda los dos lados y,
 * si uno falla, dice cuál: el borrador de ese lado queda y el del otro ya se guardó.
 */
export function SettingsSaveBar({ library, local, libraryFailure, localFailure, saving, saved, onSave, onDiscard }: Props) {
  const sides = [library ? "Biblioteca" : undefined, local ? "Esta PC" : undefined].filter((side) => side !== undefined);
  if (sides.length === 0 && libraryFailure === undefined && localFailure === undefined && !saved) {
    return null;
  }

  return (
    <div aria-label="Cambios de Ajustes" role="region" className="sticky bottom-0 space-y-1 border-t border-border bg-card p-3">
      <div className="flex flex-wrap items-center gap-2">
        <span className="mr-auto text-sm">{sides.length === 0 ? (saved ? "Guardado." : "") : `Cambios sin guardar en: ${sides.join(" / ")}`}</span>
        <Button variant="outline" onClick={onDiscard} disabled={sides.length === 0 || saving}>
          Descartar
        </Button>
        <Button onClick={onSave} disabled={sides.length === 0 || saving}>
          Guardar
        </Button>
      </div>
      {libraryFailure !== undefined && (
        <p role="alert" className="text-sm text-danger">
          No se guardó la Biblioteca: {libraryFailure.message}
        </p>
      )}
      {localFailure !== undefined && (
        <p role="alert" className="text-sm text-danger">
          No se guardó Esta PC: {localFailure.message}
        </p>
      )}
    </div>
  );
}
