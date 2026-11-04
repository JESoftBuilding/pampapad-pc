import { useId, useState } from "react";
import type { Config, State } from "@/bridge/contract";
import { consoleValue, resetConsole, setConsoleValue, type ConsolePath } from "@/model/draft";
import { consoleText } from "@/model/status";
import { Button } from "@/ui/button";
import { Field } from "@/ui/field";
import { Select } from "@/ui/select";
import { SettingsCard, toConsole } from "./SettingsCard";

type SetDraft = (change: (current: Config) => Config) => void;
type NumberPath = Exclude<ConsolePath, "muteSoundWhileRecording">;

// El bit 3 de las capacidades del Hello es "sabe dormir": la consola 0.1.5 manda 0xC7, sin él (plan de la 9b, E9b-5).
const canSleep = 0x08;

const minutes = (n: number) => n * 60;
const dimChoices = [0, 30, minutes(1), minutes(2), minutes(5), minutes(10)];
const offChoices = [0, minutes(1), minutes(2), minutes(5), minutes(10), minutes(30)];
const sleepChoices = [0, minutes(10), minutes(30), minutes(60), minutes(120)];

function timeText(seconds: number): string {
  if (seconds === 0) {
    return "Nunca";
  }

  if (seconds % 3600 === 0) {
    return `${seconds / 3600} h`;
  }

  return seconds % 60 === 0 ? `${seconds / 60} min` : `${seconds} s`;
}

function Slider({ config, setDraft, path, label, min, max, step, text }: { config: Config; setDraft: SetDraft; path: NumberPath; label: string; min: number; max: number; step: number; text: (value: number) => string }) {
  const id = useId();
  const value = consoleValue(config, path);
  return (
    <Field label={`${label}: ${text(value)}`} htmlFor={id}>
      <input
        id={id}
        type="range"
        aria-label={label}
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(event) => setDraft((current) => setConsoleValue(current, path, Number(event.target.value)))}
        className="w-full accent-accent"
      />
    </Field>
  );
}

function Time({ config, setDraft, path, label, choices, disabled = false, hint }: { config: Config; setDraft: SetDraft; path: NumberPath; label: string; choices: number[]; disabled?: boolean; hint?: string }) {
  const id = useId();
  const value = consoleValue(config, path);
  // Un valor escrito a mano que no está en la lista se sigue viendo.
  const shown = choices.includes(value) ? choices : [...choices, value].sort((a, b) => a - b);
  return (
    <Field label={label} htmlFor={id} hint={hint}>
      <Select id={id} value={value} disabled={disabled} onChange={(event) => setDraft((current) => setConsoleValue(current, path, Number(event.target.value)))}>
        {shown.map((seconds) => (
          <option key={seconds} value={seconds}>
            {timeText(seconds)}
          </option>
        ))}
      </Select>
    </Field>
  );
}

const percent = (value: number) => `${value} %`;
const holdText = (ms: number) => `${(ms / 1000).toFixed(1).replace(".", ",")} s`;

/**
 * Pantalla de la consola (05 §4.5): brillos, tiempos y holdMs del bloque console, que va por config.save y se copia a la consola con su
 * huella. Los valores de fábrica no se escriben (02 §4.10). "Volver a los valores por defecto" borra el bloque entero, también los avisos.
 */
export function ConsoleScreenCard({ config, setDraft, state }: { config: Config; setDraft: SetDraft; state?: State }) {
  const [confirming, setConfirming] = useState(false);
  const link = state?.console;
  const sleeps = ((link?.capabilities ?? 0) & canSleep) !== 0;
  const sleepHint = sleeps ? undefined : link?.capabilities === undefined ? "Se habilita cuando la consola salude" : "Esta consola no duerme";
  return (
    <SettingsCard id="pantalla-consola" title="Pantalla de la consola" place={toConsole}>
      <p className="text-sm">{link?.state === "connected" ? consoleText("connected", link.port, link) : "Sin consola: se mandan cuando se conecte"}</p>
      <div className="grid gap-3 sm:grid-cols-2">
        <Slider config={config} setDraft={setDraft} path="screen.brightness" label="Brillo" min={10} max={100} step={5} text={percent} />
        <Slider config={config} setDraft={setDraft} path="screen.dimBrightness" label="Brillo atenuado" min={0} max={50} step={5} text={percent} />
        <Time config={config} setDraft={setDraft} path="screen.dimAfterSec" label="Atenuar tras" choices={dimChoices} />
        <Time config={config} setDraft={setDraft} path="screen.offAfterSec" label="Apagar la pantalla tras" choices={offChoices} />
        <Time config={config} setDraft={setDraft} path="sleepAfterSec" label="Dormir del todo tras" choices={sleepChoices} disabled={!sleeps} hint={sleepHint} />
        <Slider config={config} setDraft={setDraft} path="holdMs" label="Mantener para PARAR y PAUSAR" min={500} max={3000} step={100} text={holdText} />
        <Slider config={config} setDraft={setDraft} path="strip.brightness" label="Brillo de la tira" min={0} max={100} step={5} text={percent} />
      </div>
      <p className="text-xs text-muted">Siempre encendida mientras se graba o se está en pausa.</p>
      {confirming ? (
        <div role="group" aria-label="Confirmar" className="flex flex-wrap items-center gap-2 text-sm">
          Se borran todos los ajustes de la consola, también los de Avisos.
          <Button
            size="sm"
            variant="danger"
            onClick={() => {
              setDraft(resetConsole);
              setConfirming(false);
            }}
          >
            Sí, volver
          </Button>
          <Button size="sm" variant="ghost" onClick={() => setConfirming(false)}>
            No
          </Button>
        </div>
      ) : (
        <Button size="sm" variant="outline" disabled={config.console === undefined} onClick={() => setConfirming(true)}>
          Volver a los valores por defecto
        </Button>
      )}
    </SettingsCard>
  );
}
