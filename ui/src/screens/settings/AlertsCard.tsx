import type { Config } from "@/bridge/contract";
import { setMuteSound } from "@/model/draft";
import { Switch } from "@/ui/switch";
import { inLibrary, SettingsCard } from "./SettingsCard";

/** Avisos (05 §4.5): callar el sonido con una toma en curso, para el zumbador y los sonidos de la PC. Por defecto, verdadero (02 §4.10). */
export function AlertsCard({ config, setDraft }: { config: Config; setDraft: (change: (current: Config) => Config) => void }) {
  const mute = config.console?.muteSoundWhileRecording !== false;
  return (
    <SettingsCard id="avisos" title="Avisos" place={inLibrary}>
      <div className="flex items-center gap-2 text-sm">
        <Switch label="Callar el sonido con una toma en curso" checked={mute} onChange={(checked) => setDraft((current) => setMuteSound(current, checked))} />
        Callar el sonido con una toma en curso
      </div>
    </SettingsCard>
  );
}
