import { useCallback, useState } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { Failure } from "@/bridge/contract";
import type { PcLightsChoice, SettingsView } from "@/bridge/contract.settings";
import type { SnapshotView } from "@/bridge/useSnapshot";

/**
 * Lo que cambia en "Esta PC": sólo lo tocado viaja en settings.save; skippedVersion "" olvida la versión salteada, consolePort "" es "Sin
 * consola" y pcLights con device y zone vacíos es "No manejar".
 */
export type LocalChanges = { startWithWindows?: boolean; skippedVersion?: string; mcpEnabled?: boolean; consolePort?: string; pcLights?: PcLightsChoice };
type LocalValue = boolean | string | PcLightsChoice;

/** "No manejar" las luces de la PC: saca pcLights de local.json. */
export const noPcLights: PcLightsChoice = { device: "", zone: "" };
export type LocalSave = { kind: "idle" } | { kind: "saving" } | { kind: "saved" } | { kind: "failed"; failure: Failure };

function same(base: SettingsView | undefined, name: keyof LocalChanges, value: LocalValue): boolean {
  if (base === undefined) {
    return false;
  }

  switch (name) {
    case "startWithWindows":
      return (base.startWithWindows === "on") === value;
    case "mcpEnabled":
      return base.mcpEnabled === value;
    case "consolePort":
      return (base.consolePort ?? "") === value;
    case "pcLights": {
      const chosen = value as PcLightsChoice;
      return (base.pcLights?.device ?? "") === chosen.device && (base.pcLights?.zone ?? "") === chosen.zone;
    }
    default:
      return (base.skippedVersion ?? "") === value;
  }
}

/** El borrador con un ajuste cambiado: si vuelve a lo que ya está guardado, sale del borrador y no viaja. */
export function withChange(current: LocalChanges, base: SettingsView | undefined, name: keyof LocalChanges, value: LocalValue): LocalChanges {
  const next: LocalChanges = { ...current };
  if (same(base, name, value)) {
    delete next[name];
  } else {
    (next as Record<string, unknown>)[name] = value;
  }

  return next;
}

/** Lo que muestran las tarjetas: la base con el borrador encima. */
export function shownOf(base: SettingsView, changes: LocalChanges): SettingsView {
  return {
    ...base,
    startWithWindows: changes.startWithWindows === undefined ? base.startWithWindows : changes.startWithWindows ? "on" : "off",
    skippedVersion: changes.skippedVersion === undefined ? base.skippedVersion : changes.skippedVersion === "" ? undefined : changes.skippedVersion,
    mcpEnabled: changes.mcpEnabled ?? base.mcpEnabled,
    consolePort: changes.consolePort === undefined ? base.consolePort : changes.consolePort === "" ? undefined : changes.consolePort,
    pcLights: changes.pcLights === undefined ? base.pcLights : changes.pcLights.device === "" ? undefined : changes.pcLights,
  };
}

/**
 * El borrador de "Esta PC" en Ajustes (05 §4.5): lo que va a local.json y el arranque con Windows, por settings.save. Se llama una sola
 * vez, en App, junto a useDraft: navegar no lo pierde. La base es lo que contestó el último settings.save o, si no hubo, la foto.
 */
export function useLocalDraft(bridge: Bridge, view: SnapshotView) {
  const [changes, setChanges] = useState<LocalChanges>({});
  const [saved, setSaved] = useState<SettingsView>();
  const [saveState, setSaveState] = useState<LocalSave>({ kind: "idle" });
  const base = saved ?? view.snapshot?.settings;

  const set = useCallback(
    (name: keyof LocalChanges, value: LocalValue) => {
      setChanges((current) => withChange(current, base, name, value));
      setSaveState({ kind: "idle" });
    },
    [base],
  );

  const dirty = Object.keys(changes).length > 0;

  /** Devuelve si se guardó. Si falla, el borrador queda. */
  const save = useCallback(async (): Promise<boolean> => {
    const sent = changes;
    setSaveState({ kind: "saving" });
    const reply = await bridge.request("settings.save", sent);
    if (!reply.ok) {
      setSaveState({ kind: "failed", failure: reply.error });
      return false;
    }

    setSaved(reply.data);
    setChanges((current) => (current === sent ? {} : current));
    setSaveState({ kind: "saved" });
    return true;
  }, [bridge, changes]);

  // update.skip contesta {} y no hay evento de ajustes: la versión salteada pasa a la base acá, o no se ve hasta reabrir.
  const markSkipped = useCallback((version: string) => setSaved(base === undefined ? undefined : { ...base, skippedVersion: version }), [base]);

  const discard = useCallback(() => {
    setChanges({});
    setSaveState({ kind: "idle" });
  }, []);

  const shown = base === undefined ? undefined : shownOf(base, changes);

  return { shown, set, dirty, save, saveState, discard, markSkipped };
}

/** Lo que devuelve useLocalDraft: App lo llama una vez y lo pasa a las pantallas por ScreenProps. */
export type LocalEditor = ReturnType<typeof useLocalDraft>;
