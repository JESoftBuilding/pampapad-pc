import type { Bridge, Reply } from "./bridge";
import { events, requests, stateSchema, type EventData, type EventName, type RequestData, type RequestName, type ResponseData, type State } from "./contract";
import samples from "./contract.samples.json";

export type FakeCall = { name: string; data: unknown };

export type FakeBridge = Bridge & {
  /** Los pedidos que hizo la página, en orden. Los mira Playwright. */
  readonly calls: FakeCall[];
  /** Simula un evento del programa (01 §7: "ver cambiar el estado con un evento simulado"). Un evento mal armado tira acá. */
  emit(event: EventName, data: unknown, seq: number): void;
  /** Cambia lo que contesta un pedido. */
  answer(name: RequestName, reply: Reply<unknown>): void;
  /** Una copia del estado de las muestras, para armar un evento a partir de él. */
  sampleState(): State;
};

/** El puente falso: contesta con las muestras de contract.samples.json, que escribe Contract.cs (01 §5). Corre en un navegador común. */
export function createFakeBridge(): FakeBridge {
  const answers = new Map<string, Reply<unknown>>();
  for (const [name, pair] of Object.entries(samples.requests)) {
    answers.set(name, { ok: true, data: pair.response.data });
  }

  const listeners = new Map<string, Set<(data: unknown, seq: number) => void>>();
  const calls: FakeCall[] = [];

  return {
    calls,

    request<N extends RequestName>(name: N, data: RequestData<N>): Promise<Reply<ResponseData<N>>> {
      calls.push({ name, data });
      const answer: Reply<unknown> = answers.get(name) ?? {
        ok: false,
        error: { code: "unknown_request", message: `El puente falso no tiene muestra de ${name}.` },
      };
      // La muestra pasa por el mismo esquema que una respuesta real, así la página recibe lo mismo de los dos puentes.
      const reply: Reply<unknown> = answer.ok ? { ok: true, data: requests[name].response.parse(answer.data) } : answer;
      return Promise.resolve(reply as Reply<ResponseData<N>>);
    },

    on<E extends EventName>(event: E, listener: (data: EventData<E>, seq: number) => void): () => void {
      const set = listeners.get(event) ?? new Set<(data: unknown, seq: number) => void>();
      listeners.set(event, set);
      const wrapped = listener as (data: unknown, seq: number) => void;
      set.add(wrapped);
      return () => {
        set.delete(wrapped);
      };
    },

    emit(event, data, seq) {
      const parsed = events[event].parse(data);
      for (const listener of listeners.get(event) ?? []) {
        listener(parsed, seq);
      }
    },

    answer(name, reply) {
      answers.set(name, reply);
    },

    sampleState() {
      return stateSchema.parse(structuredClone(samples.events.state.data));
    },
  };
}
