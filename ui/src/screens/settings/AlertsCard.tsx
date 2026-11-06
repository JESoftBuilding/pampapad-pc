import type { Config } from "@/bridge/contract";
import { alertChannel, alertIds, consoleValue, setAlertChannel, setMuteSound, type AlertId } from "@/model/draft";
import { Switch } from "@/ui/switch";
import { inLibrary, SettingsCard } from "./SettingsCard";

// Los nombres de 04 §5 en castellano, los mismos de la bandeja (AlertTexts.Name). no_pc lo decide la consola sola.
const alertNames: Record<AlertId, string> = {
  marker_failed: "La marca no entró",
  mic_muted: "Micrófono silenciado grabando",
  long_pause: "Pausa larga",
  still_recording: "Tres horas grabando",
  obs_lost: "OBS se perdió con una toma en curso",
  recording_with_failure: "Grabando con una falla",
  no_pc: "Sin PC",
};

/**
 * Avisos (05 §4.5): Luz (la tira) y Sonido (el zumbador) por aviso grave y por no_pc, en console.alerts, todos con los dos de fábrica
 * (no_pc también desde la consola 0.2.1); la pantalla va siempre. Callar
 * con una toma en curso vale sólo para los sonidos de la PC: el zumbador suena también grabando (plan de la 9b, decisión 9).
 */
export function AlertsCard({ config, setDraft }: { config: Config; setDraft: (change: (current: Config) => Config) => void }) {
  const mute = consoleValue(config, "muteSoundWhileRecording");
  return (
    <SettingsCard id="avisos" title="Avisos" place={inLibrary}>
      <div className="flex items-center gap-2 text-sm">
        <Switch label="Callar los sonidos de la PC con una toma en curso" checked={mute} onChange={(checked) => setDraft((current) => setMuteSound(current, checked))} />
        Callar los sonidos de la PC con una toma en curso
      </div>
      <p className="text-xs text-muted">El zumbador de la consola suena también grabando, si el aviso tiene Sonido.</p>
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-xs uppercase tracking-wide text-muted">
            <th className="font-normal">Aviso</th>
            <th className="font-normal">Luz</th>
            <th className="font-normal">Sonido</th>
          </tr>
        </thead>
        <tbody>
          {alertIds.map((id) => (
            <tr key={id}>
              <td className="py-1">{alertNames[id]}</td>
              {(["light", "sound"] as const).map((channel) => (
                <td key={channel} className="py-1">
                  <Switch
                    label={`${channel === "light" ? "Luz" : "Sonido"}: ${alertNames[id]}`}
                    checked={alertChannel(config, id, channel)}
                    onChange={(on) => setDraft((current) => setAlertChannel(current, id, channel, on))}
                  />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </SettingsCard>
  );
}
