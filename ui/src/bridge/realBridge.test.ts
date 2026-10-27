import { afterEach, describe, expect, it, vi } from "vitest";
import samples from "./contract.samples.json";
import { createRealBridge } from "./realBridge";
import type { WebviewLike } from "./webview";

type Listener = (event: { data: unknown }) => void;

/** Un WebView2 de mentira: anota lo que manda la página y le hace llegar lo que mandaría el programa. */
function fakeWebview() {
  const sent: unknown[] = [];
  const raw: string[] = [];
  let listener: Listener | undefined;
  const webview: WebviewLike = {
    postMessage: (message) => {
      raw.push(message as string);
      sent.push(JSON.parse(message as string));
    },
    addEventListener: (_type, handler) => {
      listener = handler;
    },
    removeEventListener: () => {
      listener = undefined;
    },
  };
  return { webview, sent, raw, deliver: (data: unknown) => listener?.({ data: JSON.stringify(data) }), listening: () => listener !== undefined };
}

afterEach(() => {
  vi.useRealTimers();
});

describe("realBridge", () => {
  it("manda el sobre de pedido con un id nuevo cada vez", () => {
    const host = fakeWebview();
    const bridge = createRealBridge(host.webview);

    void bridge.request("log.read", { count: 5 });
    void bridge.request("app.snapshot", {});

    expect(host.sent).toEqual([
      { id: 1, request: "log.read", data: { count: 5 } },
      { id: 2, request: "app.snapshot", data: {} },
    ]);
  });

  it("manda texto y no un objeto, así el orden de las claves llega intacto", () => {
    const host = fakeWebview();
    const bridge = createRealBridge(host.webview);

    void bridge.request("action.test", { action: { id: "a", steps: [{ type: "obs.scene", scene: { uuid: "u", name: "Banco" } }] } });

    expect(host.raw[0]).toBe('{"id":1,"request":"action.test","data":{"action":{"id":"a","steps":[{"type":"obs.scene","scene":{"uuid":"u","name":"Banco"}}]}}}');
  });

  it("resuelve con la respuesta que trae su id", async () => {
    const host = fakeWebview();
    const bridge = createRealBridge(host.webview);

    const reply = bridge.request("app.snapshot", {});
    host.deliver(samples.requests["app.snapshot"].response);

    const result = await reply;
    expect(result.ok).toBe(true);
    expect(result.ok && result.data.state.seq).toBe(42);
  });

  it("un error del programa vuelve como ok false, sin rechazar", async () => {
    const host = fakeWebview();
    const bridge = createRealBridge(host.webview);

    const reply = bridge.request("app.snapshot", {});
    host.deliver({ id: 1, ok: false, error: { code: "no_permission", message: "No." } });

    expect(await reply).toEqual({ ok: false, error: { code: "no_permission", message: "No." } });
  });

  it("sin respuesta en 30 s devuelve no_response y no rechaza", async () => {
    vi.useFakeTimers();
    const host = fakeWebview();
    const bridge = createRealBridge(host.webview);

    const reply = bridge.request("app.snapshot", {});
    vi.advanceTimersByTime(30_000);

    const result = await reply;
    expect(result.ok).toBe(false);
    expect(!result.ok && result.error.code).toBe("no_response");
  });

  it("una respuesta que no tiene la forma del contrato vuelve como invalid_data", async () => {
    const host = fakeWebview();
    const bridge = createRealBridge(host.webview);

    const reply = bridge.request("action.cancel", { all: true });
    host.deliver({ id: 1, ok: true, data: { cancelled: "dos" } });

    const result = await reply;
    expect(!result.ok && result.error.code).toBe("invalid_data");
  });

  it("los eventos llegan a quien escucha, con su seq; uno que no conoce se ignora", () => {
    const host = fakeWebview();
    const bridge = createRealBridge(host.webview);
    const seen: Array<[string, number]> = [];
    bridge.on("log", (entry, seq) => seen.push([entry.message, seq]));

    host.deliver(samples.events.log);
    host.deliver({ event: "script.output", seq: 43, data: {} });

    expect(seen).toEqual([["marca-short terminó bien en 18 ms.", 42]]);
  });

  it("dispose liquida los pendientes y deja de escuchar", async () => {
    const host = fakeWebview();
    const bridge = createRealBridge(host.webview);

    const reply = bridge.request("app.snapshot", {});
    bridge.dispose();

    expect(!(await reply).ok).toBe(true);
    expect(host.listening()).toBe(false);
  });
});
