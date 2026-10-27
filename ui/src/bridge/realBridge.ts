import type { Bridge, Reply } from "./bridge";
import {
  errorEnvelopeSchema,
  eventEnvelopeSchema,
  events,
  requests,
  type EventData,
  type EventName,
  type RequestData,
  type RequestName,
  type ResponseData,
} from "./contract";
import type { WebviewLike } from "./webview";

/** Tope de espera de la página: 30 s (01 §5; el número es Propuesto y sin medir). Es de la página, no de lo que se pide. */
export const replyTimeoutMs = 30_000;

/** Los pedidos que esperan algo de la persona y no del programa: scripts.import vuelve cuando se cierra el diálogo de Windows (etapa 8). */
const longReplies: Partial<Record<RequestName, number>> = { "scripts.import": 10 * 60_000 };

type Listener = (data: unknown, seq: number) => void;
type Pending = { name: RequestName; resolve: (reply: Reply<unknown>) => void; timer: ReturnType<typeof setTimeout> };

export type RealBridge = Bridge & { dispose(): void };

/** El puente de verdad: mensajes web de WebView2, con la correlación por id y los eventos propios (01 §5). */
export function createRealBridge(webview: WebviewLike, timeoutMs: number = replyTimeoutMs): RealBridge {
  let nextId = 0;
  const pending = new Map<number, Pending>();
  const listeners = new Map<string, Set<Listener>>();

  // El programa manda cada mensaje como texto (PostWebMessageAsString), para que el orden de las claves llegue intacto.
  function parse(text: string): unknown {
    try {
      return JSON.parse(text) as unknown;
    } catch {
      return undefined;
    }
  }

  function invalid(name: string, detail: string): Reply<unknown> {
    return { ok: false, error: { code: "invalid_data", message: `La respuesta a ${name} no tiene la forma del contrato: ${detail}` } };
  }

  function settle(id: number, message: Record<string, unknown>) {
    const waiting = pending.get(id);
    if (waiting === undefined) {
      return; // ya venció, o no es de esta página
    }

    pending.delete(id);
    clearTimeout(waiting.timer);
    if (message.ok === true) {
      const parsed = requests[waiting.name].response.safeParse(message.data);
      waiting.resolve(parsed.success ? { ok: true, data: parsed.data } : invalid(waiting.name, parsed.error.message));
      return;
    }

    const failed = errorEnvelopeSchema.safeParse(message);
    waiting.resolve(failed.success ? { ok: false, error: failed.data.error } : invalid(waiting.name, failed.error.message));
  }

  function deliver(message: Record<string, unknown>) {
    const envelope = eventEnvelopeSchema.safeParse(message);
    if (!envelope.success) {
      console.warn("Un evento con un sobre que no es del contrato", envelope.error.issues);
      return;
    }

    const { event, seq, data } = envelope.data;
    if (!(event in events)) {
      return; // un evento que esta página todavía no conoce
    }

    const parsed = events[event as EventName].safeParse(data);
    if (!parsed.success) {
      console.warn(`El evento ${event} trae datos que no son del contrato`, parsed.error.issues);
      return;
    }

    for (const listener of listeners.get(event) ?? []) {
      listener(parsed.data, seq);
    }
  }

  function onMessage(event: { data: unknown }) {
    const message = typeof event.data === "string" ? parse(event.data) : undefined;
    if (typeof message !== "object" || message === null) {
      return;
    }

    const record = message as Record<string, unknown>;
    if (typeof record.event === "string") {
      deliver(record);
    } else if (typeof record.id === "number") {
      settle(record.id, record);
    }
  }

  webview.addEventListener("message", onMessage);

  return {
    request<N extends RequestName>(name: N, data: RequestData<N>): Promise<Reply<ResponseData<N>>> {
      const id = ++nextId;
      const limitMs = longReplies[name] ?? timeoutMs;
      return new Promise<Reply<unknown>>((resolve) => {
        const timer = setTimeout(() => {
          pending.delete(id);
          resolve({ ok: false, error: { code: "no_response", message: `El programa no contestó ${name} en ${limitMs / 1000} s.` } });
        }, limitMs);
        pending.set(id, { name, resolve, timer });
        try {
          // Como texto y no como objeto: WebView2 convierte un objeto ordenando sus claves, y config.save reordenaría config.json.
          webview.postMessage(JSON.stringify({ id, request: name, data }));
        } catch (error) {
          pending.delete(id);
          clearTimeout(timer);
          resolve({ ok: false, error: { code: "internal_error", message: `No se pudo mandar ${name}: ${String(error)}` } });
        }
      }) as Promise<Reply<ResponseData<N>>>;
    },

    on<E extends EventName>(event: E, listener: (data: EventData<E>, seq: number) => void): () => void {
      const set = listeners.get(event) ?? new Set<Listener>();
      listeners.set(event, set);
      const wrapped = listener as Listener;
      set.add(wrapped);
      return () => {
        set.delete(wrapped);
      };
    },

    dispose() {
      webview.removeEventListener("message", onMessage);
      for (const [id, waiting] of pending) {
        clearTimeout(waiting.timer);
        pending.delete(id);
        waiting.resolve({ ok: false, error: { code: "no_response", message: "La ventana se cerró antes de la respuesta." } });
      }
    },
  };
}
