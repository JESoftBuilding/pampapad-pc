import type { Bridge } from "@/bridge/bridge";
import type { Config } from "@/bridge/contract";
import { usedByGeneral } from "@/model/hotkeys";
import { HotkeyCapture } from "@/screens/common/HotkeyCapture";
import { Button } from "@/ui/button";
import { Field } from "@/ui/field";
import { Select } from "@/ui/select";
import { cn } from "@/ui/utils";
import { inLibrary, SettingsCard } from "./SettingsCard";

type Binding = NonNullable<Config["hotkeys"]>[number];

function setBinding(config: Config, index: number, patch: Partial<Record<"hotkey" | "action", string | undefined>>): Config {
  const hotkeys = (config.hotkeys ?? []).map((binding, i) => {
    if (i !== index) {
      return binding;
    }

    const next: Binding = { ...binding };
    for (const [name, value] of Object.entries(patch)) {
      if (value === undefined || value === "") {
        delete next[name];
      } else {
        next[name] = value;
      }
    }

    return next;
  });
  return { ...config, hotkeys };
}

/** Quitar el último deja la lista sin escribir, no vacía (02 §3, regla 2). */
function removeBinding(config: Config, index: number): Config {
  const next: Config = { ...config, hotkeys: (config.hotkeys ?? []).filter((_, i) => i !== index) };
  if (next.hotkeys?.length === 0) {
    delete next.hotkeys;
  }

  return next;
}

/** Atajos de teclado generales (05 §4.5): combinación y acción, valen en cualquier página. Una combinación repetida se rechaza diciendo dónde. */
export function HotkeysCard({ config, setDraft, bridge, busyReason, highlighted }: { config: Config; setDraft: (change: (current: Config) => Config) => void; bridge: Bridge; busyReason?: string; highlighted?: number }) {
  const hotkeys = config.hotkeys ?? [];
  return (
    <SettingsCard id="atajos" title="Atajos de teclado generales" place={inLibrary}>
      {hotkeys.length === 0 && <p className="text-sm text-muted">Ninguno todavía.</p>}
      <ul aria-label="Atajos generales" className="space-y-3">
        {hotkeys.map((binding, index) => (
          <li key={index} id={`atajo-${index}`} aria-current={highlighted === index ? "true" : undefined} className={cn("grid gap-2 rounded-md p-2 sm:grid-cols-[1fr_14rem_auto]", highlighted === index && "ring-2 ring-accent/60")}>
            <HotkeyCapture
              id={`general-hotkey-${index}`}
              label={`Atajo general ${index + 1}`}
              hint="Vale en cualquier página. Esc cancela la captura."
              value={binding.hotkey}
              onChange={(hotkey) => setDraft((current) => setBinding(current, index, { hotkey }))}
              bridge={bridge}
              disabledReason={busyReason}
              taken={(text) => usedByGeneral(config, index, text)}
              clearable={false}
            />
            <Field label={`Acción del atajo ${index + 1}`} htmlFor={`general-action-${index}`}>
              <Select id={`general-action-${index}`} value={binding.action ?? ""} onChange={(event) => setDraft((current) => setBinding(current, index, { action: event.target.value }))}>
                <option value="">(elegí una acción)</option>
                {(config.actions ?? []).map((action) => (
                  <option key={action.id} value={action.id}>
                    {action.name ?? action.id}
                  </option>
                ))}
              </Select>
            </Field>
            <Button size="sm" variant="ghost" className="self-end" onClick={() => setDraft((current) => removeBinding(current, index))}>
              Quitar
            </Button>
          </li>
        ))}
      </ul>
      <Button size="sm" variant="outline" onClick={() => setDraft((current) => ({ ...current, hotkeys: [...(current.hotkeys ?? []), {}] }))}>
        Agregar atajo
      </Button>
    </SettingsCard>
  );
}
