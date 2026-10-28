import type { ScriptManifest } from "@/bridge/contract.scripts";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import { Field } from "@/ui/field";
import { Input } from "@/ui/input";
import { Select } from "@/ui/select";
import { Switch } from "@/ui/switch";
import { Textarea } from "@/ui/textarea";
import type { ScriptEditor } from "./useScriptDraft";

type Place = { kind: string; where: string; page?: string; pageName?: string; position?: number; hotkey?: string };

/** Cada renglón de "Quién lo usa" lleva a su pantalla (05 §4.3): la tecla a Teclas y el atajo general a su tarjeta de Ajustes. */
function placeLink(place: Place): string | undefined {
  const hotkey = /^hotkeys\[(\d+)\]/.exec(place.where);
  if (place.kind === "hotkey") {
    return hotkey === null ? undefined : `#/settings?hotkey=${hotkey[1]}`;
  }

  return place.page === undefined || place.position === undefined ? undefined : `#/keys?page=${encodeURIComponent(place.page)}&position=${place.position}`;
}

function placeText(place: Place): string {
  return place.kind === "hotkey" ? `atajo general ${place.hotkey ?? ""}` : `${place.pageName ?? place.page ?? "?"}, tecla ${place.position ?? "?"}${place.kind === "hold" ? " al mantener" : ""}`;
}

/** Un campo de script.json: vacío se quita (02 §3, regla 2); lo que la ventana no conoce queda como estaba. */
function withField(manifest: ScriptManifest, name: string, value: unknown): ScriptManifest {
  const next: Record<string, unknown> = { ...manifest };
  if (value === undefined || value === "" || (Array.isArray(value) && value.length === 0)) {
    delete next[name];
  } else {
    next[name] = value;
  }

  return next as ScriptManifest;
}

/** La ficha (05 §4.3): los campos de script.json, los archivos, quién lo usa, la última salida y las cinco últimas corridas. */
export function ScriptCard({ ficha, removeReason, onSaved, onRemove, onReveal }: { ficha: ScriptEditor; removeReason?: string; onSaved: () => void; onRemove: () => void; onReveal: () => void }) {
  const open = ficha.open;
  if (open === undefined) {
    return <p className="text-sm text-muted">Elegí un script de la lista.</p>;
  }

  const { view, read, draft } = open;
  const problems = view.problems.length > 0 && (
    <ul className="space-y-1 text-sm text-danger">
      {view.problems.map((problem) => (
        <li key={problem.code + problem.message}>{problem.message}</li>
      ))}
    </ul>
  );
  if (view.status === "noManifest" || draft === undefined || read === undefined) {
    return (
      <div className="space-y-2">
        <h2 className="text-lg">{view.name}</h2>
        {view.status === "noManifest" && <p className="text-sm text-muted">Sin ficha: no se puede probar ni elegir en un paso hasta que tenga su carpeta con script.json.</p>}
        {problems}
        {open.failure !== undefined && <p className="text-sm text-danger">No se pudo leer la ficha: {open.failure.message}</p>}
      </div>
    );
  }

  const set = (name: string, value: unknown) => ficha.setManifest((current) => withField(current, name, value));
  const entries = read.files.filter((file) => file.name !== "script.json");
  const failure = ficha.saveState.kind === "failed" ? ficha.saveState.failure : undefined;
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <h2 className="mr-auto text-lg">{view.name}</h2>
        <Button size="sm" variant="outline" onClick={onReveal}>
          Abrir carpeta
        </Button>
        <Button size="sm" variant="outline" onClick={onRemove} disabled={removeReason !== undefined} title={removeReason}>
          Quitar
        </Button>
      </div>
      {removeReason !== undefined && <p className="text-xs text-muted">{removeReason}</p>}
      {problems}
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Nombre" htmlFor="script-name">
          <Input id="script-name" value={draft.name ?? ""} onChange={(event) => set("name", event.target.value)} />
        </Field>
        <Field label="Entrada" htmlFor="script-entry">
          <Select id="script-entry" value={draft.entry ?? ""} onChange={(event) => set("entry", event.target.value)}>
            <option value="">(elegí un archivo)</option>
            {entries.map((file) => (
              <option key={file.name} value={file.name}>
                {file.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Descripción" htmlFor="script-description" className="sm:col-span-2">
          <Textarea id="script-description" value={draft.description ?? ""} onChange={(event) => set("description", event.target.value)} />
        </Field>
        <Field label="Tope en segundos" htmlFor="script-timeout">
          <Input id="script-timeout" type="number" min={1} value={draft.timeoutSec ?? ""} placeholder={String(view.timeoutSec)} onChange={(event) => set("timeoutSec", event.target.value === "" ? undefined : Number(event.target.value))} />
        </Field>
        <Field label="Etiquetas" htmlFor="script-tags" hint="Separadas por coma.">
          <Input id="script-tags" value={(draft.tags ?? []).join(", ")} onChange={(event) => set("tags", event.target.value.split(",").map((tag) => tag.trim()).filter((tag) => tag !== ""))} />
        </Field>
        <div className="flex items-center gap-2 text-sm">
          <Switch label="Esperar a que termine" checked={draft.wait ?? view.wait} onChange={(checked) => set("wait", checked)} /> Esperar a que termine
        </div>
        <p className="text-sm text-muted">Contrato {view.contract ?? "?"} (sólo lectura)</p>
      </div>
      <div className="flex flex-wrap items-center gap-2 border-t border-border pt-3">
        <Button onClick={() => void ficha.save().then((saved) => saved && onSaved())} disabled={!ficha.dirty || ficha.saveState.kind === "saving"}>
          Guardar la ficha
        </Button>
        <Button variant="outline" onClick={ficha.discard} disabled={!ficha.dirty}>
          Descartar
        </Button>
        {ficha.dirty && <Badge tone="warn">cambios sin guardar</Badge>}
        {ficha.saveState.kind === "saved" && <span className="text-sm text-ok">Guardado.</span>}
      </div>
      {failure !== undefined && (
        <div role="alert" className="text-sm text-danger">
          No se guardó: {failure.message}{" "}
          {failure.code === "config_changed_on_disk" && (
            <Button size="sm" variant="outline" onClick={() => void ficha.reload()}>
              Recargar la ficha
            </Button>
          )}
        </div>
      )}
      <section aria-label="Archivos" className="text-sm">
        <h3 className="text-xs uppercase tracking-wide text-muted">Archivos</h3>
        <ul>
          {read.files.map((file) => (
            <li key={file.name}>
              <span className="font-mono text-xs">{file.name}</span> <span className="text-muted">· {file.size} bytes · {new Date(file.modified).toLocaleString("es-AR")}</span>
            </li>
          ))}
        </ul>
      </section>
      <section aria-label="Quién lo usa" className="text-sm">
        <h3 className="text-xs uppercase tracking-wide text-muted">Quién lo usa</h3>
        {view.usedBy.length === 0 && <p className="text-muted">Ninguna acción.</p>}
        <ul>
          {view.usedBy.map((use) => (
            <li key={use.action}>
              <a href={`#/actions?id=${encodeURIComponent(use.action)}`} className="underline">
                {use.actionName ?? use.action}
              </a>
              {use.places.map((place) => {
                const link = placeLink(place);
                return (
                  <span key={place.where}>
                    {" · "}
                    {link === undefined ? placeText(place) : <a href={link} className="underline">{placeText(place)}</a>}
                  </span>
                );
              })}
            </li>
          ))}
        </ul>
      </section>
      <section aria-label="Últimas corridas" className="text-sm">
        <h3 className="text-xs uppercase tracking-wide text-muted">Últimas corridas</h3>
        {read.runs.length === 0 && <p className="text-muted">Nunca corrió en esta PC (el registro guarda 14 días).</p>}
        {read.runs[0]?.end.message !== undefined && <p>Última salida: {read.runs[0].end.message}</p>}
        <ul>
          {read.runs.map((run) => (
            <li key={run.at}>
              <Badge tone={run.end.ok ? "ok" : "bad"}>{run.end.ok ? "bien" : "falló"}</Badge> {new Date(run.at).toLocaleString("es-AR")} · {run.end.durationMs} ms
              {run.requestedBy !== undefined && <span className="text-muted"> · {run.requestedBy}</span>}
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
