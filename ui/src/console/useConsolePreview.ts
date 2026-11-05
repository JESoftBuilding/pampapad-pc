import { useEffect, useState } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { Config } from "@/bridge/contract";
import type { ConsoleFrame, PreviewSample } from "@/bridge/contract.console";

/** La espera desde el último cambio del borrador hasta pedir la vista previa (plan de la 9b, E9b-6). */
export const PREVIEW_WAIT_MS = 200;

/**
 * La vista previa de la consola con el borrador, no con lo guardado (plan de la 9b, decisión 13): se pide de nuevo a los 200 ms del último
 * cambio del borrador, de la página o del estado de muestra. Una respuesta que llega después de otro cambio no pisa la nueva.
 */
export function useConsolePreview(bridge: Bridge, draft: Config | undefined, page: string | undefined, sample: PreviewSample) {
  const [frame, setFrame] = useState<ConsoleFrame>();
  useEffect(() => {
    if (draft === undefined) {
      return;
    }

    let current = true;
    const timer = window.setTimeout(() => {
      void bridge.request("console.preview", { config: draft, state: sample, ...(page === undefined ? {} : { page }) }).then((reply) => {
        // shortcut: un borrador que no se lee deja la última vista previa; el problema ya lo muestra el editor.
        if (current && reply.ok) setFrame(reply.data);
      });
    }, PREVIEW_WAIT_MS);
    return () => {
      current = false;
      window.clearTimeout(timer);
    };
  }, [bridge, draft, page, sample]);
  return frame;
}
