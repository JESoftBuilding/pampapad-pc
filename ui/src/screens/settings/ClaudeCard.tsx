import { useState } from "react";
import type { SettingsView } from "@/bridge/contract.settings";
import { Button } from "@/ui/button";
import { Switch } from "@/ui/switch";
import { onlyThisPc, SettingsCard } from "./SettingsCard";

type Props = { settings?: SettingsView; onMcpEnabled: (on: boolean) => void };

/**
 * Claude (05 §4.5 y §7): el interruptor del MCP y el comando para registrarlo en Claude Code. Apagado, la tubería sigue abierta y todo
 * pedido de Claude recibe no_permission. El comando falta en la copia de desarrollo, que no corre desde una instalación.
 */
export function ClaudeCard({ settings, onMcpEnabled }: Props) {
  const [copied, setCopied] = useState<"yes" | "failed">();
  const command = settings?.mcpCommand;

  const copy = async () => {
    if (command === undefined) {
      return;
    }

    try {
      await navigator.clipboard.writeText(command);
      setCopied("yes");
    } catch {
      setCopied("failed");
    }
  };

  return (
    <SettingsCard id="claude" title="Claude" place={onlyThisPc}>
      <div className="flex items-center gap-2 text-sm">
        <Switch label="Permitir que Claude maneje PampaPad" checked={settings?.mcpEnabled ?? false} disabled={settings === undefined} onChange={onMcpEnabled} />
        Permitir que Claude maneje PampaPad
      </div>
      <p className="text-xs text-muted">Mientras se graba: sólo lectura</p>
      {command === undefined ? (
        <p className="text-sm text-muted">No disponible en la copia de desarrollo</p>
      ) : (
        <div className="space-y-1 text-sm">
          <p>Para registrarlo en Claude Code, una vez:</p>
          <div className="flex flex-wrap items-center gap-2">
            <code className="break-all font-mono text-xs">{command}</code>
            <Button size="sm" variant="outline" onClick={() => void copy()}>
              Copiar
            </Button>
            {copied === "yes" && <span className="text-xs text-muted">Copiado</span>}
            {copied === "failed" && (
              <span role="alert" className="text-xs text-danger">
                No se pudo copiar: seleccionalo y copialo a mano.
              </span>
            )}
          </div>
        </div>
      )}
    </SettingsCard>
  );
}
