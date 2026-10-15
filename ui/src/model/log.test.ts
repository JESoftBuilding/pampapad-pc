import { describe, expect, it } from "vitest";
import { keepLast, matches, mergeLog } from "./log";

const line = (level: "info" | "warning" | "error", requestedBy?: string, runId?: string) => ({ at: "2026-10-14T23:30:00-03:00", level, requestedBy, runId });

describe("matches, el mismo criterio que LogQuery", () => {
  it("el nivel es un mínimo", () => {
    expect(matches(line("info"), { level: "warning" })).toBe(false);
    expect(matches(line("warning"), { level: "warning" })).toBe(true);
    expect(matches(line("error"), { level: "warning" })).toBe(true);
  });

  it("requestedBy es exacto", () => {
    expect(matches(line("info", "key"), { level: "info", requestedBy: "key" })).toBe(true);
    expect(matches(line("info", "hotkey"), { level: "info", requestedBy: "key" })).toBe(false);
  });

  it("runId es exacto", () => {
    expect(matches(line("info", "key", "a"), { level: "info", runId: "a" })).toBe(true);
    expect(matches(line("info", "key", "b"), { level: "info", runId: "a" })).toBe(false);
  });

  it("sin filtros pasa todo", () => {
    expect(matches(line("info"), { level: "info" })).toBe(true);
  });
});

describe("keepLast", () => {
  it("suma al final", () => {
    expect(keepLast([1, 2], 3)).toEqual([1, 2, 3]);
  });

  it("se queda con las últimas", () => {
    expect(keepLast([1, 2, 3], 4, 3)).toEqual([2, 3, 4]);
  });
});

describe("mergeLog", () => {
  it("de lo que llegó en vivo, deja sólo lo posterior a lo leído", () => {
    const read = [{ at: "2026-10-14T23:30:00-03:00", n: 1 }, { at: "2026-10-14T23:30:05-03:00", n: 2 }];
    const live = [{ at: "2026-10-14T23:30:05-03:00", n: 2 }, { at: "2026-10-14T23:30:06-03:00", n: 3 }];

    expect(mergeLog(read, live).map((entry) => entry.n)).toEqual([1, 2, 3]);
  });

  it("sin lectura queda lo vivo", () => {
    expect(mergeLog([], [{ at: "2026-10-14T23:30:00-03:00" }])).toHaveLength(1);
  });
});
