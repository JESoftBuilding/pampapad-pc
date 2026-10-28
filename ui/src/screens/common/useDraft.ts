import { useCallback, useEffect, useRef, useState } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { Action, ActionFinished, Config, Failure, Problem } from "@/bridge/contract";
import type { SnapshotView } from "@/bridge/useSnapshot";
import { sameConfig } from "@/model/draft";

export type SaveState = { kind: "idle" } | { kind: "saving" } | { kind: "saved"; problems: Problem[] } | { kind: "failed"; failure: Failure };

export type RunState =
  | { kind: "idle" }
  | { kind: "starting" }
  | { kind: "running"; runId: string }
  | { kind: "finished"; result: ActionFinished }
  | { kind: "refused"; failure: Failure };

type Edit = { draft: Config; base: { config: Config; fingerprint: string } };

/** Probar y grabar un atajo se apagan con una toma en curso (05 §4.1 y §4.2; la captura, Propuesto: cierra el Incierto de 05). */
export const busyText = "Se apaga mientras grabás, estás en pausa o transmitís.";

/**
 * El borrador de config.json que editan Teclas y Acciones (01 §1: "pide una foto, edita un borrador y devuelve el objeto completo").
 * Se llama una sola vez, en App: 05 §11 define un solo window.dirty por ventana, y cambiar de pantalla no descarta lo editado.
 * La base es la foto de la que salió, con su huella: config.save manda esa huella y no la última que llegó, para que un cambio en el
 * disco mientras se editaba falle con config_changed_on_disk en vez de pisarse (02 §3, regla 11). Una foto nueva reemplaza el borrador
 * sólo si no tiene cambios; si los tiene, queda y la barra avisa que está atrasado.
 */
export function useDraft(bridge: Bridge, view: SnapshotView) {
  const [edit, setEdit] = useState<Edit>();
  const [saveState, setSaveState] = useState<SaveState>({ kind: "idle" });
  const [run, setRun] = useState<RunState>({ kind: "idle" });
  const finished = useRef(new Map<string, ActionFinished>());
  const snapshot = view.snapshot;
  const missing = edit === undefined;

  useEffect(() => {
    if (snapshot === undefined) {
      return;
    }

    const fresh: Edit = { draft: snapshot.config, base: { config: snapshot.config, fingerprint: snapshot.fingerprint } };
    setEdit((current) => (current === undefined || sameConfig(current.draft, current.base.config) ? fresh : current));
  }, [snapshot, missing]);

  const dirty = edit !== undefined && !sameConfig(edit.draft, edit.base.config);
  const outdated = edit !== undefined && snapshot !== undefined && edit.base.fingerprint !== snapshot.fingerprint && saveState.kind !== "saving";

  // El resultado de una prueba puede llegar antes que la respuesta con su runId: se guarda y se mira al llegar la respuesta.
  useEffect(
    () =>
      bridge.on("action.finished", (result) => {
        finished.current.set(result.runId, result);
        setRun((current) => (current.kind === "running" && current.runId === result.runId ? { kind: "finished", result } : current));
      }),
    [bridge],
  );

  const setDraft = useCallback((change: (current: Config) => Config) => {
    setEdit((current) => (current === undefined ? current : { ...current, draft: change(current.draft) }));
    setSaveState({ kind: "idle" });
  }, []);

  /** Devuelve si se guardó: la barra de Ajustes dice de qué lado falló. */
  const save = useCallback(async (): Promise<boolean> => {
    if (edit === undefined) {
      return false;
    }

    const sent = edit.draft;
    setSaveState({ kind: "saving" });
    const reply = await bridge.request("config.save", { config: sent, expectedFingerprint: edit.base.fingerprint });
    if (!reply.ok) {
      setSaveState({ kind: "failed", failure: reply.error });
      return false;
    }

    // Lo que quedó escrito es lo que la foto trae si ya llegó (en forma canónica); si no, el borrador tal cual.
    const written = snapshot !== undefined && snapshot.fingerprint === reply.data.fingerprint ? snapshot.config : sent;
    // Lo que se escribió mientras guardaba no se pisa: queda como cambio sin guardar sobre lo escrito.
    setEdit((current) => ({
      draft: current === undefined || current.draft === sent ? written : current.draft,
      base: { config: written, fingerprint: reply.data.fingerprint },
    }));
    setSaveState({ kind: "saved", problems: reply.data.problems });
    return true;
  }, [bridge, edit, snapshot]);

  const discard = useCallback(() => {
    setEdit(undefined);
    setSaveState({ kind: "idle" });
  }, []);

  const reload = useCallback(async () => {
    discard();
    const reply = await bridge.request("library.reload", {});
    if (!reply.ok) {
      setSaveState({ kind: "failed", failure: reply.error });
    }
  }, [bridge, discard]);

  const test = useCallback(
    async (action: Action) => {
      setRun({ kind: "starting" });
      const reply = await bridge.request("action.test", { action });
      if (!reply.ok) {
        setRun({ kind: "refused", failure: reply.error });
        return;
      }

      const already = finished.current.get(reply.data.runId);
      setRun(already === undefined ? { kind: "running", runId: reply.data.runId } : { kind: "finished", result: already });
    },
    [bridge],
  );

  const cancelTest = useCallback(async () => {
    if (run.kind === "running") {
      await bridge.request("action.cancel", { runId: run.runId });
    }
  }, [bridge, run]);

  const take = view.state !== undefined && (view.state.obs.takeInProgress || view.state.obs.streaming);
  return {
    draft: edit?.draft,
    loaded: snapshot?.config,
    setDraft,
    dirty,
    outdated,
    save,
    saveState,
    discard,
    reload,
    test,
    cancelTest,
    run,
    busyReason: take ? busyText : undefined,
    problems: view.state?.problems ?? [],
  };
}

/** Lo que devuelve useDraft: App lo llama una vez y lo pasa a las pantallas por ScreenProps. */
export type Editor = ReturnType<typeof useDraft>;
