/** Lo que la página usa de window.chrome.webview (WebView2, 01 §5). No hay paquete de tipos oficial: se declara lo mínimo. */
export interface WebviewLike {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
  removeEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebviewLike };
  }
}

/** El canal con el programa, si la página corre dentro de WebView2; undefined en un navegador común. */
export function hostWebview(): WebviewLike | undefined {
  return typeof window === "undefined" ? undefined : window.chrome?.webview;
}
