import type { State } from "@/bridge/contract";
import type { SettingsView } from "@/bridge/contract.settings";
import { obsText } from "@/model/status";
import { onlyThisPc, SettingsCard } from "./SettingsCard";

/** OBS (05 §4.5), de sólo lectura en esta etapa (decisión 39): la dirección que se usa, de dónde sale la clave y el estado del enlace. */
export function ObsCard({ settings, state }: { settings?: SettingsView; state?: State }) {
  return (
    <SettingsCard id="obs" title="OBS" place={onlyThisPc}>
      <dl className="grid grid-cols-[8rem_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted">Dirección</dt>
        <dd className="font-mono text-xs">{settings?.obs?.address ?? "automática"}</dd>
        <dt className="text-muted">Clave</dt>
        <dd>{settings?.obs?.password ?? "la guardada en OBS"}</dd>
        <dt className="text-muted">Enlace</dt>
        <dd>{state === undefined ? "?" : obsText(state.obs.link)}</dd>
      </dl>
    </SettingsCard>
  );
}
