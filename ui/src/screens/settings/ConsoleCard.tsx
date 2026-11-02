import { useEffect, useId, useState } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { State } from "@/bridge/contract";
import type { ConsolePort, SettingsView } from "@/bridge/contract.settings";
import { Field } from "@/ui/field";
import { Select } from "@/ui/select";
import { onlyThisPc, SettingsCard } from "./SettingsCard";

type Props = { bridge: Bridge; settings?: SettingsView; state?: State; onPort: (port: string) => void };

const linkText = { off: "Sin consola", waiting: "Esperando la consola", connected: "Conectada", released: "Soltada" } as const;

/**
 * Conexión con la consola (05 §4.5): el puerto de ahora con su estado y la lista de los que hay, con la descripción de Windows. Va con el
 * borrador de Esta PC, por settings.save con consolePort. En la 8b no hay "Automático" ni "Soltar": sin saludo de la placa no se la puede
 * reconocer, y soltar es de la bandeja. El puerto se lee al arrancar, así que el elegido rige al volver a abrir (plan de la 8b, decisión 3).
 */
export function ConsoleCard({ bridge, settings, state, onPort }: Props) {
  const id = useId();
  const [ports, setPorts] = useState<ConsolePort[]>();
  const [failure, setFailure] = useState<string>();

  useEffect(() => {
    let alive = true;
    void bridge.request("console.ports", {}).then((reply) => {
      if (alive) {
        setPorts(reply.ok ? reply.data.ports : []);
        setFailure(reply.ok ? undefined : reply.error.message);
      }
    });
    return () => {
      alive = false;
    };
  }, [bridge]);

  const chosen = settings?.consolePort ?? "";
  const missing = chosen !== "" && ports !== undefined && !ports.some((p) => p.port === chosen);
  return (
    <SettingsCard id="consola" title="Conexión con la consola" place={onlyThisPc}>
      {state !== undefined && (
        <p className="text-sm">
          {state.console.port === undefined ? linkText.off : `${state.console.port} · ${linkText[state.console.state]}`}
        </p>
      )}
      <p className="text-xs text-muted">Conectada quiere decir que el puerto abrió: la placa todavía no saluda, y las luces no avisan si recibieron.</p>
      <Field label="Puerto de la consola" htmlFor={id} error={failure === undefined ? undefined : `No se pudo leer la lista de puertos: ${failure}`}>
        <Select id={id} value={chosen} disabled={settings === undefined} onChange={(event) => onPort(event.target.value)}>
          <option value="">Sin consola</option>
          {missing && <option value={chosen}>{chosen} (no está)</option>}
          {(ports ?? []).map((p) => (
            <option key={p.port} value={p.port}>
              {p.description === "" ? p.port : `${p.port} · ${p.description}`}
            </option>
          ))}
        </Select>
      </Field>
      {ports?.length === 0 && failure === undefined && <p className="text-sm text-muted">No hay puertos serie</p>}
      <p className="text-xs text-muted">Se aplica al volver a abrir PampaPad</p>
    </SettingsCard>
  );
}
