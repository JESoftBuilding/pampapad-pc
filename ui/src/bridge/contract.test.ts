import { describe, expect, it } from "vitest";
import samples from "./contract.samples.json";
import { errorEnvelopeSchema, eventEnvelope, events, requestEnvelope, requests, responseEnvelope, type EventName, type RequestName } from "./contract";

// Valida cada muestra de contract.samples.json, que escribe tests/PampaPad.Tests/Contract.cs, contra los esquemas de contract.ts (01 §5).
// Si C# cambia un record y esto no, falla acá; si cambia esto y no C#, falla Contract.cs.
//
// Pasar el esquema no alcanza: los z.object de respuestas y eventos dejan pasar campos de más y descartan en silencio las claves que
// no conocen (E6-9). Un campo renombrado o nuevo en C# daría "sin errores" y la página leería undefined. Por eso cada prueba compara
// además lo que devuelve el parseo con la muestra tal cual: una clave que contract.ts no tiene aparece como diferencia.
describe("contract.samples.json contra contract.ts", () => {
  it("tiene una muestra por cada pedido de contract.ts, y ninguna de más", () => {
    expect(Object.keys(samples.requests).sort()).toEqual(Object.keys(requests).sort());
  });

  it("tiene una muestra por cada evento de contract.ts, y ninguna de más", () => {
    expect(Object.keys(samples.events).sort()).toEqual(Object.keys(events).sort());
  });

  for (const [name, pair] of Object.entries(samples.requests)) {
    it(`${name}: el pedido y la respuesta pasan sus esquemas, sin perder ninguna clave`, () => {
      const schemas = requests[name as RequestName];
      const request = requestEnvelope(name, schemas.request).safeParse(pair.request);
      expect(request.error?.issues ?? []).toEqual([]);
      expect(request.data).toStrictEqual(pair.request);
      const response = responseEnvelope(schemas.response).safeParse(pair.response);
      expect(response.error?.issues ?? []).toEqual([]);
      expect(response.data).toStrictEqual(pair.response);
    });
  }

  for (const [name, envelope] of Object.entries(samples.events)) {
    it(`evento ${name}: pasa su esquema, sin perder ninguna clave`, () => {
      const parsed = eventEnvelope(name, events[name as EventName]).safeParse(envelope);
      expect(parsed.error?.issues ?? []).toEqual([]);
      expect(parsed.data).toStrictEqual(envelope);
    });
  }

  it("el error pasa el esquema del sobre de error, sin perder ninguna clave", () => {
    const parsed = errorEnvelopeSchema.safeParse(samples.error);
    expect(parsed.error?.issues ?? []).toEqual([]);
    expect(parsed.data).toStrictEqual(samples.error);
  });

  it("un guardado rechazado trae todos sus problemas, sin perder ninguna clave", () => {
    const parsed = errorEnvelopeSchema.safeParse(samples.saveRejected);
    expect(parsed.error?.issues ?? []).toEqual([]);
    expect(parsed.data).toStrictEqual(samples.saveRejected);
    expect(parsed.data?.error.problems?.map((problem) => problem.severity)).toEqual(["error", "warning"]);
  });
});
