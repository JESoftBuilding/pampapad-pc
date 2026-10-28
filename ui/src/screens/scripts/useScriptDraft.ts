import { useCallback, useState } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { Failure, ResponseData } from "@/bridge/contract";
import type { ScriptManifest, ScriptView } from "@/bridge/contract.scripts";
import { sameConfig } from "@/model/draft";

export type ScriptRead = ResponseData<"scripts.read">;
export type FichaSave = { kind: "idle" } | { kind: "saving" } | { kind: "saved" } | { kind: "failed"; failure: Failure };

/** Lo abierto en la ficha: la fila de la lista y, si tiene ficha, lo que trajo scripts.read y el borrador de script.json. */
type Open = { view: ScriptView; read?: ScriptRead; draft?: ScriptManifest; failure?: Failure };

/**
 * El borrador de la ficha de un script (05 §4.3). Se llama una sola vez, en App, junto a useDraft: navegar a otra pantalla no lo pierde
 * (useDraft.ts). scripts.write manda la huella de lo leído, como config.save: un cambio en el disco mientras se editaba falla con
 * config_changed_on_disk (05 §11) y la barra ofrece recargar la ficha. Abrir otro script descarta lo que no se guardó.
 */
export function useScriptDraft(bridge: Bridge) {
  const [open, setOpen] = useState<Open>();
  const [saveState, setSaveState] = useState<FichaSave>({ kind: "idle" });

  const select = useCallback(
    async (view: ScriptView | undefined) => {
      setSaveState({ kind: "idle" });
      if (view === undefined || view.status === "noManifest") {
        // Un archivo suelto no tiene ficha que leer (script_without_manifest): se muestra la fila con lo que le falta.
        setOpen(view === undefined ? undefined : { view });
        return;
      }

      setOpen({ view });
      const reply = await bridge.request("scripts.read", { id: view.id });
      setOpen((current) => {
        if (current?.view.id !== view.id) {
          return current;
        }

        return reply.ok ? { view: reply.data.script, read: reply.data, draft: reply.data.manifest ?? {} } : { view, failure: reply.error };
      });
    },
    [bridge],
  );

  const setManifest = useCallback((change: (current: ScriptManifest) => ScriptManifest) => {
    setOpen((current) => (current?.draft === undefined ? current : { ...current, draft: change(current.draft) }));
    setSaveState({ kind: "idle" });
  }, []);

  const dirty = open?.draft !== undefined && !sameConfig(open.draft, open.read?.manifest ?? {});

  /** Devuelve si se guardó: la pantalla vuelve a pedir la lista para ver el nombre nuevo. */
  const save = useCallback(async (): Promise<boolean> => {
    if (open?.read === undefined || open.draft === undefined) {
      return false;
    }

    const sent = open.draft;
    const id = open.view.id;
    setSaveState({ kind: "saving" });
    const reply = await bridge.request("scripts.write", { id, manifest: sent, expectedFingerprint: open.read.fingerprint });
    if (!reply.ok) {
      setSaveState({ kind: "failed", failure: reply.error });
      return false;
    }

    setOpen((current) =>
      current?.read === undefined || current.view.id !== id ? current : { ...current, read: { ...current.read, manifest: sent, fingerprint: reply.data.fingerprint } },
    );
    setSaveState({ kind: "saved" });
    return true;
  }, [bridge, open]);

  const discard = useCallback(() => {
    setOpen((current) => (current?.read === undefined ? current : { ...current, draft: current.read.manifest ?? {} }));
    setSaveState({ kind: "idle" });
  }, []);

  /** Recargar la ficha: lee otra vez del disco y tira el borrador (la salida de config_changed_on_disk). */
  const reload = useCallback(() => (open === undefined ? Promise.resolve() : select(open.view)), [open, select]);

  /** La fila nueva de la lista reemplaza la abierta: después de guardar o de una corrida, el título y "Lo usan" no quedan viejos. */
  const refreshView = useCallback((views: ScriptView[]) => {
    setOpen((current) => {
      const fresh = current === undefined ? undefined : views.find((view) => view.id === current.view.id);
      return current === undefined || fresh === undefined || fresh === current.view ? current : { ...current, view: fresh };
    });
  }, []);

  return { open, select, setManifest, dirty, save, saveState, discard, reload, refreshView };
}

/** Lo que devuelve useScriptDraft: App lo llama una vez y lo pasa a las pantallas por ScreenProps. */
export type ScriptEditor = ReturnType<typeof useScriptDraft>;
