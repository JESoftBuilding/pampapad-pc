import { Button } from "@/ui/button";
import { onlyThisPc, SettingsCard } from "./SettingsCard";

/** Datos (05 §4.5): la carpeta de la biblioteca, abrirla y recargarla. Cambiarla se hace desde la bandeja. */
export function DataCard({ folder, reloadBlocked, onReveal, onReload }: { folder?: string; reloadBlocked?: string; onReveal: () => void; onReload: () => void }) {
  return (
    <SettingsCard id="datos" title="Datos" place={onlyThisPc}>
      <p className="font-mono text-xs">{folder === undefined || folder === "" ? "(sin carpeta elegida)" : folder}</p>
      <div className="flex flex-wrap gap-2">
        <Button size="sm" variant="outline" onClick={onReveal}>
          Abrir carpeta
        </Button>
        <Button size="sm" variant="outline" disabled={reloadBlocked !== undefined} onClick={onReload}>
          Recargar
        </Button>
      </div>
      {reloadBlocked !== undefined && <p className="text-xs text-muted">{reloadBlocked}</p>}
      <p className="text-xs text-muted">Para usar otra carpeta: en el ícono de la bandeja, "Elegir carpeta de datos".</p>
    </SettingsCard>
  );
}
