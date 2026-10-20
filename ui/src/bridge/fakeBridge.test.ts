import { describe, expect, it } from "vitest";
import { createFakeBridge } from "./fakeBridge";

describe("fakeBridge", () => {
  it("contesta cada pedido con su muestra", async () => {
    const bridge = createFakeBridge();

    const reply = await bridge.request("app.snapshot", {});

    expect(reply.ok && reply.data.version).toBe("0.2.1");
  });

  it("anota los pedidos en orden", async () => {
    const bridge = createFakeBridge();

    await bridge.request("action.cancel", { all: true });
    await bridge.request("window.ready", {});

    expect(bridge.calls).toEqual([
      { name: "action.cancel", data: { all: true } },
      { name: "window.ready", data: {} },
    ]);
  });

  it("emit valida el evento y se lo da a quien escucha con su seq", () => {
    const bridge = createFakeBridge();
    const seqs: number[] = [];
    bridge.on("state", (state, seq) => seqs.push(state.seq + seq));

    bridge.emit("state", { ...bridge.sampleState(), seq: 43 }, 43);

    expect(seqs).toEqual([86]);
    expect(() => bridge.emit("state", { seq: "no" }, 44)).toThrow();
  });

  it("answer cambia lo que contesta un pedido", async () => {
    const bridge = createFakeBridge();
    bridge.answer("action.cancel", { ok: false, error: { code: "internal_error", message: "Se rompió." } });

    const reply = await bridge.request("action.cancel", { all: true });

    expect(reply).toEqual({ ok: false, error: { code: "internal_error", message: "Se rompió." } });
  });

  it("sampleState da una copia: cambiarla no cambia la muestra", () => {
    const bridge = createFakeBridge();

    bridge.sampleState().basePage = "otra";

    expect(bridge.sampleState().basePage).toBe("grabacion");
  });
});
