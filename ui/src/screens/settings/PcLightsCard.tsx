import { useEffect, useId, useState } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { State } from "@/bridge/contract";
import type { PcLightsChoice, SettingsView } from "@/bridge/contract.settings";
import { pcLightsText } from "@/model/status";
import { Field } from "@/ui/field";
import { Select } from "@/ui/select";
import { noPcLights } from "./useLocalDraft";
import { onlyThisPc, SettingsCard } from "./SettingsCard";

type Props = { bridge: Bridge; settings?: SettingsView; state?: State; onChoose: (choice: PcLightsChoice) => void };
type Zones = { kind: "loading" } | { kind: "ready"; zones: PcLightsChoice[]; unavailable?: string } | { kind: "failed"; message: string };

const label = (choice: PcLightsChoice) => `${choice.device} · ${choice.zone}`;
// Un valor por zona para el <select>: device y zone juntos, porque dos dispositivos pueden tener una zona con el mismo nombre.
const key = (choice: PcLightsChoice) => JSON.stringify([choice.device, choice.zone]);

/**
 * Luces de la PC (05 §4.5; plan de la 1.6.0, decisión 7): "No manejar" y las zonas que da OpenRGB, por options.list. Va con el borrador
 * de Esta PC, por settings.save con pcLights, y vale en el acto. Si OpenRGB no responde lo dice y la zona elegida se sigue viendo, aunque
 * no esté en la lista. Con OpenRGB caído la lista puede tardar un intento de conexión: mientras, dice que está buscando.
 */
export function PcLightsCard({ bridge, settings, state, onChoose }: Props) {
  const id = useId();
  const [zones, setZones] = useState<Zones>({ kind: "loading" });

  useEffect(() => {
    let alive = true;
    void bridge.request("options.list", { list: "pcLightZones" }).then((reply) => {
      if (alive) {
        setZones(
          reply.ok
            ? { kind: "ready", zones: reply.data.items.map((item) => ({ device: item.detail ?? "", zone: item.name })), unavailable: reply.data.unavailable }
            : { kind: "failed", message: reply.error.message },
        );
      }
    });
    return () => {
      alive = false;
    };
  }, [bridge]);

  const chosen = settings?.pcLights;
  const listed = zones.kind === "ready" ? zones.zones : [];
  const missing = chosen !== undefined && zones.kind !== "loading" && !listed.some((zone) => key(zone) === key(chosen));
  const byKey = new Map(listed.map((zone) => [key(zone), zone]));
  if (chosen !== undefined) {
    byKey.set(key(chosen), chosen);
  }

  const unavailable = zones.kind === "ready" ? zones.unavailable : undefined;
  return (
    <SettingsCard id="luces-pc" title="Luces de la PC" place={onlyThisPc}>
      {state !== undefined && <p className="text-sm">{pcLightsText(state.pcLights.state, state.pcLights.zone)}</p>}
      <p className="text-xs text-muted">Siguen a las luces de la sala, por OpenRGB.</p>
      <Field label="Zona" htmlFor={id} error={zones.kind === "failed" ? `No se pudo leer la lista de zonas: ${zones.message}` : undefined}>
        <Select
          id={id}
          value={chosen === undefined ? "" : key(chosen)}
          disabled={settings === undefined}
          onChange={(event) => onChoose(byKey.get(event.target.value) ?? noPcLights)}
        >
          <option value="">No manejar</option>
          {chosen !== undefined && (zones.kind === "loading" || missing) && (
            <option value={key(chosen)}>
              {label(chosen)}
              {missing ? " (no está)" : ""}
            </option>
          )}
          {listed.map((zone) => (
            <option key={key(zone)} value={key(zone)}>
              {label(zone)}
            </option>
          ))}
        </Select>
      </Field>
      {zones.kind === "loading" && <p className="text-sm text-muted">Buscando las zonas en OpenRGB…</p>}
      {unavailable !== undefined && (
        <p className="text-sm text-muted">
          {unavailable}
          {chosen === undefined ? "" : " Se conserva la zona elegida."}
        </p>
      )}
      {zones.kind === "ready" && unavailable === undefined && listed.length === 0 && <p className="text-sm text-muted">OpenRGB no tiene zonas</p>}
    </SettingsCard>
  );
}
