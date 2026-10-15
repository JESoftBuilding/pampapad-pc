import { describe, expect, it } from "vitest";
import { snapshotSchema, stateSchema } from "./contract";
import samples from "./contract.samples.json";
import { newer, reduce } from "./useSnapshot";

const snapshot = snapshotSchema.parse(samples.requests["app.snapshot"].response.data);
const state = (seq: number) => ({ ...stateSchema.parse(samples.events.state.data), seq });

describe("newer", () => {
  it("toma el primero que llega", () => {
    expect(newer(undefined, state(1)).seq).toBe(1);
  });

  it("toma uno con seq mayor", () => {
    expect(newer(state(42), state(43)).seq).toBe(43);
  });

  it("descarta un estado con seq menor o igual", () => {
    const current = state(42);

    expect(newer(current, state(42))).toBe(current);
    expect(newer(current, state(41))).toBe(current);
  });
});

describe("reduce", () => {
  it("la foto trae el estado y la configuración", () => {
    const view = reduce({}, { kind: "snapshot", snapshot });

    expect(view.state?.seq).toBe(42);
    expect(view.snapshot?.version).toBe("0.2.1");
  });

  it("un estado más nuevo que llegó antes que la foto se queda", () => {
    const early = reduce({}, { kind: "state", state: state(50) });

    expect(reduce(early, { kind: "snapshot", snapshot }).state?.seq).toBe(50);
  });

  it("config.changed reemplaza la configuración de la foto", () => {
    const view = reduce(reduce({}, { kind: "snapshot", snapshot }), { kind: "config", config: { mainPage: "otra" } });

    expect(view.snapshot?.config).toEqual({ mainPage: "otra" });
  });

  it("una falla al pedir la foto queda a la vista y no borra el estado", () => {
    const view = reduce(reduce({}, { kind: "state", state: state(7) }), { kind: "failed", failure: { code: "no_response", message: "Nada." } });

    expect(view.failure?.code).toBe("no_response");
    expect(view.state?.seq).toBe(7);
  });
});
