import { useCallback, useEffect, useState } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { ActionFinished, LogEntry } from "@/bridge/contract";
import { keepLast, matches, mergeLog, type LogFilter } from "@/model/log";

/** Lo que Estado junta además de la foto (05 §4.4): el registro en vivo, las acciones que terminan y cancelar. El componente sólo pinta. */
export function useStatusModel(bridge: Bridge) {
  const [entries, setEntries] = useState<LogEntry[]>([]);
  const [finished, setFinished] = useState<ActionFinished[]>([]);
  const [filter, setFilter] = useState<LogFilter>({ level: "info" });
  const [notice, setNotice] = useState<string>();

  useEffect(() => {
    let alive = true;
    // Primero se escucha y después se lee: lo que llegue en el medio se junta sin repetir (mergeLog).
    const offLog = bridge.on("log", (entry) => setEntries((lines) => keepLast(lines, entry)));
    const offFinished = bridge.on("action.finished", (result) => setFinished((done) => keepLast(done, result, 5)));
    void bridge.request("log.read", { count: 200 }).then((reply) => {
      if (!alive) {
        return;
      }

      if (reply.ok) {
        setEntries((live) => mergeLog(reply.data.entries, live));
      } else {
        setNotice(`No se pudo leer el registro: ${reply.error.message}`);
      }
    });

    return () => {
      alive = false;
      offLog();
      offFinished();
    };
  }, [bridge]);

  const cancel = useCallback(
    async (runId?: string) => {
      const reply = await bridge.request("action.cancel", runId === undefined ? { all: true } : { runId });
      if (!reply.ok) {
        setNotice(`No se pudo cancelar: ${reply.error.message}`);
        return;
      }

      const count = reply.data.cancelled;
      setNotice(count === 0 ? "No había nada en curso: ya había terminado." : `Se pidió cancelar ${count} ${count === 1 ? "acción" : "acciones"}.`);
    },
    [bridge],
  );

  return { entries: entries.filter((entry) => matches(entry, filter)), filter, setFilter, finished, notice, cancel };
}
