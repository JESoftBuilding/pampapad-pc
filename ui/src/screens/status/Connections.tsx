import type { Snapshot, State } from "@/bridge/contract";
import { consoleText, hotkeysText, obsText, recordingText } from "@/model/status";
import { Card, CardTitle } from "@/ui/card";

/**
 * Conexiones (05 §4.4): OBS, la consola y los atajos, que son las dos fuentes de entrada de hoy. Los atajos que rechazó Windows son los
 * problemas hotkey_taken del estado (AppState.Problems): se listan con su ubicación debajo de la cuenta.
 */
export function Connections({ state, settings }: { state: State; settings: Snapshot["settings"] }) {
  const { obs } = state;
  const scene = obs.link === "ready" && obs.sceneName !== undefined ? `escena ${obs.sceneName}` : undefined;
  const take = recordingText(obs.link, obs.recording, obs.recordedMs, obs.streaming);
  const taken = state.problems.filter((problem) => problem.code === "hotkey_taken");
  return (
    <Card>
      <CardTitle>Conexiones</CardTitle>
      <dl className="grid grid-cols-[7rem_1fr] gap-x-4 gap-y-2 text-sm">
        <dt className="text-muted">OBS</dt>
        <dd>{[obsText(obs.link), scene, take].filter((part) => part !== undefined).join(" · ")}</dd>
        <dt className="text-muted">Micrófono</dt>
        <dd>{obs.micName === undefined ? "sin dato" : `${obs.micName}${obs.micMuted ? ", silenciado" : ""}`}</dd>
        <dt className="text-muted">Consola</dt>
        <dd>{consoleText(state.console.state, state.console.port)}</dd>
        <dt className="text-muted">Atajos</dt>
        <dd>
          {hotkeysText(settings.hotkeysEnabled, state.hotkeys.registered, state.hotkeys.rejected)}
          {taken.length > 0 && (
            <ul aria-label="Atajos que rechazó Windows" className="mt-1 space-y-1 text-xs text-muted">
              {taken.map((problem) => (
                <li key={problem.where}>
                  <span className="font-mono">{problem.where}</span> {problem.message}
                </li>
              ))}
            </ul>
          )}
        </dd>
        <dt className="text-muted">Página</dt>
        <dd>{state.activePage === "" ? "ninguna" : state.activePage}</dd>
      </dl>
    </Card>
  );
}
