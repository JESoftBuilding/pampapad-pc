import type { EventData, EventName, Failure, RequestData, RequestName, ResponseData } from "./contract";
import { createFakeBridge, type FakeBridge } from "./fakeBridge";
import { createRealBridge } from "./realBridge";
import { hostWebview } from "./webview";

/** Lo que vuelve de un pedido. Nunca rechaza: un error o el tope de espera vuelven como ok false (01 §5). */
export type Reply<T> = { ok: true; data: T } | { ok: false; error: Failure };

/** El puente visto desde la página. realBridge y fakeBridge implementan este mismo tipo (01 §1, regla 4). */
export interface Bridge {
  request<N extends RequestName>(name: N, data: RequestData<N>): Promise<Reply<ResponseData<N>>>;
  on<E extends EventName>(event: E, listener: (data: EventData<E>, seq: number) => void): () => void;
}

declare global {
  interface Window {
    pampapadFake?: FakeBridge;
  }
}

/**
 * El puente de esta página: el real dentro de WebView2 y el falso en un navegador común (05 §10). Sólo con el falso queda colgado de
 * window.pampapadFake, para que Playwright simule eventos; dentro de WebView2 no existe.
 */
export function pickBridge(): Bridge {
  const webview = hostWebview();
  if (webview !== undefined) {
    return createRealBridge(webview);
  }

  const fake = createFakeBridge();
  window.pampapadFake = fake;
  return fake;
}
