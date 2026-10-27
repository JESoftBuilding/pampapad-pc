import { useCallback, useEffect, useState } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { RequestData, ResponseData } from "@/bridge/contract";

export type OptionsName = RequestData<"options.list">["list"];
export type OptionItem = ResponseData<"options.list">["items"][number];
type Options = { items: OptionItem[]; unavailable?: string; loading: boolean; error?: string };

/** Las opciones de un campo (05 §4.2): options.list al montar y a pedido. Sin OBS, unavailable trae el motivo y el campo acepta texto. */
export function useOptions(bridge: Bridge, list: OptionsName) {
  const [options, setOptions] = useState<Options>({ items: [], loading: true });

  const refresh = useCallback(() => {
    setOptions((current) => ({ ...current, loading: true }));
    void bridge.request("options.list", { list }).then((reply) => {
      setOptions(reply.ok ? { items: reply.data.items, unavailable: reply.data.unavailable, loading: false } : { items: [], loading: false, error: reply.error.message });
    });
  }, [bridge, list]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  return { ...options, refresh };
}
