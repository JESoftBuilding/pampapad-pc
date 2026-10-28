import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { Failure } from "@/bridge/contract";
import type { ScriptEnd, ScriptView } from "@/bridge/contract.scripts";
import { filterScripts, sortScripts, type ScriptOrder, type Situation } from "@/model/scripts";
import type { ScriptEditor } from "./useScriptDraft";

/** El corte de la salida en vivo, el mismo del emisor de script.output (plan de la etapa 8, "Una salida enorme"). */
export const outputLimit = 2000;

export type OutputLine = { line: string; stream: "stdout" | "stderr" | "notice" };
type Output = { lines: OutputLine[]; end?: ScriptEnd };
export type TestState =
  | { kind: "idle" }
  | { kind: "starting"; id: string }
  | { kind: "running"; id: string; runId: string; output: Output }
  | { kind: "refused"; id: string; failure: Failure };

/**
 * Lo que Scripts junta además del borrador de la ficha (05 §4.3): la lista entera de scripts.list, que se filtra acá; la prueba con
 * su salida en vivo; importar, quitar y abrir la carpeta. La salida puede llegar antes que la respuesta de scripts.test con su runId:
 * se guarda por runId y se mira al llegar la respuesta, como action.finished en useDraft.
 */
export function useScriptsModel(bridge: Bridge, ficha: ScriptEditor) {
  const [scripts, setScripts] = useState<ScriptView[]>();
  const [listFailure, setListFailure] = useState<Failure>();
  const [text, setText] = useState("");
  const [tag, setTag] = useState("");
  const [situation, setSituation] = useState<Situation>("all");
  const [order, setOrder] = useState<ScriptOrder>("name");
  const [test, setTest] = useState<TestState>({ kind: "idle" });
  const [actionFailure, setActionFailure] = useState<Failure>();
  const outputs = useRef(new Map<string, Output>());

  const refresh = useCallback(async () => {
    const reply = await bridge.request("scripts.list", {});
    if (reply.ok) {
      setScripts(reply.data.scripts);
      setListFailure(undefined);
    } else {
      setListFailure(reply.error);
    }

    return reply.ok ? reply.data.scripts : undefined;
  }, [bridge]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(
    () =>
      bridge.on("script.output", (event) => {
        const current = outputs.current.get(event.runId) ?? { lines: [] };
        let next: Output = current;
        if (event.end !== undefined) {
          next = { ...current, end: event.end };
        } else if (event.line !== undefined && current.lines.length < outputLimit) {
          const lines = [...current.lines, { line: event.line, stream: event.stream ?? "stdout" }];
          next = { ...current, lines: lines.length === outputLimit ? [...lines, { line: `Se cortó en ${outputLimit} líneas.`, stream: "notice" }] : lines };
        }

        outputs.current.set(event.runId, next);
        setTest((state) => (state.kind === "running" && state.runId === event.runId ? { ...state, output: next } : state));
      }),
    [bridge],
  );

  const startTest = useCallback(
    async (id: string) => {
      setTest({ kind: "starting", id });
      const reply = await bridge.request("scripts.test", { id });
      if (!reply.ok) {
        setTest({ kind: "refused", id, failure: reply.error });
        return;
      }

      setTest({ kind: "running", id, runId: reply.data.runId, output: outputs.current.get(reply.data.runId) ?? { lines: [] } });
    },
    [bridge],
  );

  const cancelTest = useCallback(async () => {
    if (test.kind === "running" && test.output.end === undefined) {
      await bridge.request("action.cancel", { runId: test.runId });
    }
  }, [bridge, test]);

  const act = useCallback(
    async (work: () => Promise<{ ok: boolean; error?: Failure }>) => {
      setActionFailure(undefined);
      const reply = await work();
      if (!reply.ok) {
        setActionFailure(reply.error);
      }

      return reply.ok;
    },
    [],
  );

  /** Importar espera el diálogo de Windows (tope de 10 minutos en realBridge) y, al volver, pide la lista otra vez. */
  const importScripts = useCallback(async () => {
    const reply = await bridge.request("scripts.import", {});
    setActionFailure(reply.ok ? undefined : reply.error);
    const list = await refresh();
    const imported = reply.ok && reply.data.id !== "" ? list?.find((script) => script.id === reply.data.id) : undefined;
    if (imported !== undefined) {
      void ficha.select(imported);
    }
  }, [bridge, ficha, refresh]);

  const remove = useCallback(
    async (id: string) => {
      if (await act(() => bridge.request("scripts.delete", { id }))) {
        void ficha.select(undefined);
        await refresh();
      }
    },
    [act, bridge, ficha, refresh],
  );

  const reveal = useCallback((id?: string) => act(() => bridge.request("scripts.reveal", id === undefined ? {} : { id })), [act, bridge]);

  const shown = useMemo(() => sortScripts(filterScripts(scripts ?? [], { text, tag, situation }), order), [scripts, text, tag, situation, order]);
  const tags = useMemo(() => [...new Set((scripts ?? []).flatMap((script) => script.tags))].sort(), [scripts]);

  return {
    scripts,
    shown,
    tags,
    listFailure,
    filter: { text, setText, tag, setTag, situation, setSituation, order, setOrder },
    refresh,
    test,
    startTest,
    cancelTest,
    importScripts,
    remove,
    reveal,
    actionFailure,
  };
}

export type ScriptsModel = ReturnType<typeof useScriptsModel>;
