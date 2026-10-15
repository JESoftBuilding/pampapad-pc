import { describe, expect, it } from "vitest";
import { cn } from "./utils";

describe("cn", () => {
  it("junta clases y, cuando dos chocan, deja la última", () => {
    expect(cn("px-2 py-1", "px-4")).toBe("py-1 px-4");
  });

  it("ignora lo que viene apagado", () => {
    expect(cn("a", false, undefined, "b")).toBe("a b");
  });
});
