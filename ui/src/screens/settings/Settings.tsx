import { useEffect, useState } from "react";
import { queryOf } from "@/model/where";
import type { ScreenProps } from "@/screens/screen";
import { Card } from "@/ui/card";
import { AlertsCard } from "./AlertsCard";
import { DataCard } from "./DataCard";
import { HotkeysCard } from "./HotkeysCard";
import { ObsCard } from "./ObsCard";
import { PagesCard } from "./PagesCard";
import { ProgramCard } from "./ProgramCard";
import { SettingsSaveBar } from "./SettingsSaveBar";

function hotkeyOfHash(): number | undefined {
  const value = queryOf(window.location.hash).get("hotkey");
  return value === null || Number.isNaN(Number(value)) ? undefined : Number(value);
}

/**
 * La pantalla Ajustes (05 §4.5): una página que se desplaza, con una tarjeta por tema. Lo de la biblioteca va por config.save con el
 * borrador compartido de Teclas y Acciones; lo de esta PC, por settings.save con el suyo. Los dos borradores viven en App.
 */
export function SettingsScreen({ bridge, view, editor, local }: ScreenProps) {
  const [highlighted, setHighlighted] = useState(hotkeyOfHash);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [programFailure, setProgramFailure] = useState<string>();

  // Un enlace de Estado (05 §4.4) llega por el hash: #/settings?hotkey=1 lleva a ese atajo general.
  useEffect(() => {
    const onHash = () => setHighlighted(hotkeyOfHash());
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  const ready = editor.draft !== undefined;
  useEffect(() => {
    if (highlighted !== undefined && ready) {
      document.getElementById(`atajo-${highlighted}`)?.scrollIntoView({ block: "center" });
    }
  }, [highlighted, ready]);

  if (view.snapshot === undefined || editor.draft === undefined) {
    return (
      <Card>
        <p>{view.failure === undefined ? "Pidiendo la configuración al programa…" : `No se pudo leer la configuración: ${view.failure.message}`}</p>
      </Card>
    );
  }

  // Primero la biblioteca y después esta PC; cada uno se manda aunque el otro falle (con una toma en curso config.save se niega).
  const save = async () => {
    setSaving(true);
    setSaved(false);
    const library = editor.dirty ? await editor.save() : true;
    const pc = local.dirty ? await local.save() : true;
    setSaving(false);
    setSaved(library && pc);
  };

  const run = async (name: "update.check" | "update.skip" | "library.reveal") => {
    const reply = await bridge.request(name, {});
    setProgramFailure(reply.ok ? undefined : reply.error.message);
    const update = view.state?.update;
    if (reply.ok && name === "update.skip" && update?.phase === "available" && update.version !== undefined) {
      local.markSkipped(update.version);
    }
  };

  const config = editor.draft;
  return (
    <div className="space-y-4">
      <PagesCard config={config} setDraft={editor.setDraft} />
      <HotkeysCard config={config} setDraft={editor.setDraft} bridge={bridge} busyReason={editor.busyReason} highlighted={highlighted} />
      <AlertsCard config={config} setDraft={editor.setDraft} />
      <ObsCard settings={local.shown} state={view.state} />
      <DataCard
        folder={view.state?.library.folder}
        reloadBlocked={editor.dirty ? "Recargar tira los cambios sin guardar de la biblioteca: primero guardá o descartá." : undefined}
        onReveal={() => void run("library.reveal")}
        onReload={() => void editor.reload()}
      />
      <ProgramCard
        settings={local.shown}
        version={view.snapshot.version}
        update={view.state?.update}
        failure={programFailure}
        onStartWithWindows={(on) => local.set("startWithWindows", on)}
        onCheck={() => void run("update.check")}
        onSkip={() => void run("update.skip")}
        onForget={() => local.set("skippedVersion", "")}
      />
      <SettingsSaveBar
        library={editor.dirty}
        local={local.dirty}
        libraryFailure={editor.saveState.kind === "failed" ? editor.saveState.failure : undefined}
        localFailure={local.saveState.kind === "failed" ? local.saveState.failure : undefined}
        saving={saving}
        saved={saved}
        onSave={() => void save()}
        onDiscard={() => {
          editor.discard();
          local.discard();
          setSaved(false);
        }}
      />
    </div>
  );
}
