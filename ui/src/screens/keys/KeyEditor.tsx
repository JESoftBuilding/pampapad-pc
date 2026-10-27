import type { Bridge } from "@/bridge/bridge";
import type { Action, Key, Page, Problem } from "@/bridge/contract";
import type { KeyPatch } from "@/model/draft";
import { fitsScreen, lineLength, shortLabelFor } from "@/model/labels";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import { Field } from "@/ui/field";
import { Input } from "@/ui/input";
import { Select } from "@/ui/select";
import type { OptionItem } from "../common/useOptions";
import { HotkeyCapture } from "./HotkeyCapture";

type Props = {
  page: Page;
  position: number;
  current?: Key;
  actions: Action[];
  scripts: { items: OptionItem[]; unavailable?: string };
  problems: Problem[];
  bridge: Bridge;
  busyReason?: string;
  takenBy: (hotkey: string) => string | null;
  onChange: (patch: KeyPatch) => void;
  onScript: (scriptId: string, name: string) => void;
  onRemove: () => void;
};

/** El editor de la tecla elegida (05 §4.1). Lo que se escribe queda en el borrador; Guardar manda la configuración entera. */
export function KeyEditor({ page, position, current, actions, scripts, problems, bridge, busyReason, takenBy, onChange, onScript, onRemove }: Props) {
  const computed = shortLabelFor(current?.label ?? "");
  const lines = current?.shortLabel ?? [];
  const setLine = (index: number, value: string) => {
    const next = [lines[0] ?? "", lines[1] ?? ""];
    next[index] = value.toUpperCase();
    while (next.length > 0 && next[next.length - 1] === "") {
      next.pop();
    }

    onChange({ shortLabel: next.length === 0 ? undefined : next });
  };
  const choose = (value: string) => {
    if (value.startsWith("script:")) {
      const id = value.slice("script:".length);
      onScript(id, scripts.items.find((item) => item.name === id)?.detail ?? id);
    } else {
      onChange({ action: value === "" ? undefined : value });
    }
  };
  const editable = page.type === undefined || page.type === "keys";
  return (
    <div className="space-y-4">
      <h2 className="font-display text-sm uppercase tracking-wide text-muted">
        Tecla {position} de {page.name ?? page.id}
      </h2>
      {!editable && <p className="text-sm text-accent">Esta página es de tipo {page.type}: la maneja la consola y no se edita acá (02 §4.2).</p>}
      <Field label="Etiqueta" htmlFor="key-label" hint="Como se escribió, con acentos: la consola la abrevia sola.">
        <Input id="key-label" value={current?.label ?? ""} onChange={(event) => onChange({ label: event.target.value === "" ? undefined : event.target.value })} disabled={!editable} />
      </Field>
      <Field
        label="En la pantalla de la consola"
        hint={`Si se deja vacío, se calcula de la etiqueta: ${computed.join(" / ")}. Dos líneas de hasta ${lineLength} letras, en mayúsculas y sin acentos.`}
        error={lines.length > 0 && !fitsScreen(lines) ? `Hasta ${lineLength} letras por línea.` : undefined}
      >
        <div className="flex gap-2">
          {[0, 1].map((index) => (
            <Input
              key={index}
              aria-label={`Línea ${index + 1}`}
              className="w-28 font-display uppercase"
              maxLength={lineLength}
              placeholder={computed[index] ?? ""}
              value={lines[index] ?? ""}
              onChange={(event) => setLine(index, event.target.value)}
              disabled={!editable}
            />
          ))}
        </div>
      </Field>
      <Field label="Qué hace" htmlFor="key-action" hint={scripts.unavailable}>
        <Select id="key-action" value={current?.action ?? ""} onChange={(event) => choose(event.target.value)} disabled={!editable}>
          <option value="">— Nada —</option>
          <optgroup label="Acciones">
            {actions.map((action) => (
              <option key={action.id} value={action.id}>
                {action.name ?? action.id}
              </option>
            ))}
          </optgroup>
          <optgroup label="Scripts">
            {scripts.items.map((script) => (
              <option key={script.name} value={`script:${script.name}`}>
                {script.detail ?? script.name} ({script.name})
              </option>
            ))}
          </optgroup>
          <optgroup label="Agentes">
            <option disabled>Más adelante</option>
          </optgroup>
        </Select>
      </Field>
      <Field label="Al mantener" htmlFor="key-hold" hint="Parar y pausar van acá: con un toque, el validador no deja guardar (02 §6).">
        <Select
          id="key-hold"
          value={current?.onHold?.action ?? ""}
          onChange={(event) => onChange({ onHold: event.target.value === "" ? undefined : { ...current?.onHold, action: event.target.value } })}
          disabled={!editable}
        >
          <option value="">— Nada —</option>
          {actions.map((action) => (
            <option key={action.id} value={action.id}>
              {action.name ?? action.id}
            </option>
          ))}
        </Select>
      </Field>
      <HotkeyCapture key={`${page.id}|${position}`} value={current?.hotkey} onChange={(hotkey) => onChange({ hotkey })} bridge={bridge} disabledReason={busyReason} taken={takenBy} />
      <dl className="grid grid-cols-[8rem_1fr] gap-x-4 text-sm">
        <dt className="text-muted">La ejecuta</dt>
        <dd>{editable ? "La PC" : "La consola sola"}</dd>
      </dl>
      {problems.length > 0 && (
        <ul aria-label="Problemas de la tecla" className="space-y-1 text-sm">
          {problems.map((problem) => (
            <li key={`${problem.where}|${problem.code}`}>
              <Badge tone={problem.severity === "error" ? "bad" : "warn"}>{problem.severity === "error" ? "error" : "aviso"}</Badge> {problem.message}
            </li>
          ))}
        </ul>
      )}
      <Button size="sm" variant="ghost" onClick={onRemove} disabled={current === undefined || !editable}>
        Quitar la tecla
      </Button>
    </div>
  );
}
