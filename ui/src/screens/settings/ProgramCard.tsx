import type { State } from "@/bridge/contract";
import type { SettingsView } from "@/bridge/contract.settings";
import { Button } from "@/ui/button";
import { Switch } from "@/ui/switch";
import { onlyThisPc, SettingsCard } from "./SettingsCard";

/** El motivo de la copia de desarrollo (05 §10): no toca el registro de Windows ni busca actualizaciones. */
export const devCopyText = "En la copia de desarrollo no se toca el arranque con Windows ni se buscan actualizaciones.";

type Props = {
  settings?: SettingsView;
  version?: string;
  update?: State["update"];
  failure?: string;
  onStartWithWindows: (on: boolean) => void;
  onCheck: () => void;
  onSkip: () => void;
  onForget: () => void;
};

/** Programa (05 §4.5): el arranque con Windows, la versión instalada, Buscar actualización y la versión salteada. */
export function ProgramCard({ settings, version, update, failure, onStartWithWindows, onCheck, onSkip, onForget }: Props) {
  const dev = settings?.startWithWindows === "unavailable";
  return (
    <SettingsCard id="programa" title="Programa" place={onlyThisPc}>
      <div className="flex items-center gap-2 text-sm">
        <Switch label="Arrancar con Windows" checked={settings?.startWithWindows === "on"} disabled={dev || settings === undefined} onChange={onStartWithWindows} />
        Arrancar con Windows
      </div>
      {dev && <p className="text-xs text-muted">{devCopyText}</p>}
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <span>Versión instalada {version ?? "?"}</span>
        <Button size="sm" variant="outline" onClick={onCheck} disabled={dev || update?.phase === "checking"}>
          Buscar actualización
        </Button>
        {update?.phase === "checking" && <span className="text-muted">Buscando…</span>}
      </div>
      {update?.phase === "available" && (
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span>Hay una versión nueva: {update.version}</span>
          <Button size="sm" variant="outline" onClick={onSkip}>
            Saltear esta versión
          </Button>
        </div>
      )}
      {settings?.skippedVersion !== undefined && (
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span>Versión salteada: {settings.skippedVersion}</span>
          <Button size="sm" variant="ghost" onClick={onForget}>
            Olvidar la versión salteada
          </Button>
        </div>
      )}
      {failure !== undefined && (
        <p role="alert" className="text-sm text-danger">
          {failure}
        </p>
      )}
    </SettingsCard>
  );
}
