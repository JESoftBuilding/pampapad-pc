// Dónde se usa una acción (05 §4.2): página y tecla, al mantener, o atajo general. Se calcula de la configuración al listar y no se
// guarda en ningún lado (05 §4.3, "Quién lo usa"). Puro y sin importar nada.

type KeyLike = { position?: number; action?: string; onHold?: { action?: string } };
type PageLike = { id?: string; name?: string; keys?: KeyLike[] };
type BindingLike = { hotkey?: string; action?: string };
export type ConfigLike = { pages?: PageLike[]; hotkeys?: BindingLike[] };

export type Usage =
  | { kind: "key"; pageId: string; pageName: string; position: number; hold: boolean }
  | { kind: "hotkey"; index: number; hotkey: string };

export function usageOf(config: ConfigLike, actionId: string): Usage[] {
  const found: Usage[] = [];
  for (const page of config.pages ?? []) {
    for (const key of page.keys ?? []) {
      if (key.position === undefined) {
        continue;
      }

      if (key.action === actionId) {
        found.push({ kind: "key", pageId: page.id ?? "", pageName: page.name ?? "", position: key.position, hold: false });
      }

      if (key.onHold?.action === actionId) {
        found.push({ kind: "key", pageId: page.id ?? "", pageName: page.name ?? "", position: key.position, hold: true });
      }
    }
  }

  (config.hotkeys ?? []).forEach((binding, index) => {
    if (binding.action === actionId) {
      found.push({ kind: "hotkey", index, hotkey: binding.hotkey ?? "" });
    }
  });

  return found;
}

export function usageText(usage: Usage): string {
  if (usage.kind === "hotkey") {
    return `atajo general ${usage.hotkey}`;
  }

  const page = usage.pageName.length > 0 ? usage.pageName : usage.pageId;
  return `${page}, tecla ${usage.position}${usage.hold ? " al mantener" : ""}`;
}
