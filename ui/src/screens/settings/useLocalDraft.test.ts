import { describe, expect, it } from "vitest";
import samples from "@/bridge/contract.samples.json";
import { settingsViewSchema } from "@/bridge/contract.settings";
import { noPcLights, shownOf, withChange } from "./useLocalDraft";

// La base de la muestra tiene elegida "ASUS ROG STRIX B450-F GAMING II · RGB Header"; sin pcLights, es "No manejar".
const chosen = settingsViewSchema.parse(samples.requests["app.snapshot"].response.data.settings);
const without = { ...chosen, pcLights: undefined };
const fans = { device: "Corsair", zone: "Fans" };

describe("el borrador de Esta PC con las luces de la PC (1.6.0)", () => {
  it("elegir una zona pone pcLights con device y zone en lo que viaja en settings.save", () => {
    const changes = withChange({}, without, "pcLights", fans);

    expect(changes).toEqual({ pcLights: fans });
    expect(shownOf(without, changes).pcLights).toEqual(fans);
  });

  it("No manejar manda device y zone vacíos, que sacan pcLights, y la tarjeta ya no muestra zona", () => {
    const changes = withChange({}, chosen, "pcLights", noPcLights);

    expect(changes).toEqual({ pcLights: { device: "", zone: "" } });
    expect(shownOf(chosen, changes).pcLights).toBeUndefined();
  });

  it("volver a lo guardado lo saca del borrador: no viaja", () => {
    expect(withChange({ pcLights: fans }, without, "pcLights", noPcLights)).toEqual({});
    expect(withChange({}, chosen, "pcLights", { device: "ASUS ROG STRIX B450-F GAMING II", zone: "RGB Header" })).toEqual({});
  });
});
