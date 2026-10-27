// De la ubicación de un problema (pages[0].keys[2].hotkey) al enlace de la pieza que lo tiene (05 §4.4: "lleva a la tecla o a la
// acción"). Las listas se nombran por índice en el archivo y la pantalla por id y posición, que no cambian al reordenar. La lista
// general hotkeys se edita en Ajustes (etapa 8): no tiene enlace todavía. Puro y sin importar nada.

type KeyLike = { position?: number };
type PageLike = { id?: string; keys?: KeyLike[] };
type ActionLike = { id?: string };
export type ConfigLike = { pages?: PageLike[]; actions?: ActionLike[] };

export function linkFor(where: string, config: ConfigLike): string | undefined {
  const key = /^pages\[(\d+)\]\.keys\[(\d+)\]/.exec(where);
  if (key !== null) {
    const page = config.pages?.[Number(key[1])];
    const position = page?.keys?.[Number(key[2])]?.position;
    return page?.id === undefined || position === undefined ? undefined : `#/keys?page=${encodeURIComponent(page.id)}&position=${position}`;
  }

  const action = /^actions\[(\d+)\]/.exec(where);
  if (action !== null) {
    const id = config.actions?.[Number(action[1])]?.id;
    return id === undefined ? undefined : `#/actions?id=${encodeURIComponent(id)}`;
  }

  return undefined;
}

/** Lo que sigue al signo de pregunta de un hash como #/keys?page=grabacion&position=5. */
export function queryOf(hash: string): URLSearchParams {
  const at = hash.indexOf("?");
  return new URLSearchParams(at < 0 ? "" : hash.slice(at + 1));
}

/** La ubicación de una tecla en el archivo (pages[i].keys[j]), para juntar sus problemas, que vienen con los índices del archivo cargado. */
export function whereOfKey(config: ConfigLike, pageId: string, position: number): string | undefined {
  const pageIndex = config.pages?.findIndex((page) => page.id === pageId) ?? -1;
  const keyIndex = pageIndex < 0 ? -1 : (config.pages?.[pageIndex].keys?.findIndex((key) => key.position === position) ?? -1);
  return keyIndex < 0 ? undefined : `pages[${pageIndex}].keys[${keyIndex}]`;
}

export function whereOfAction(config: ConfigLike, id: string): string | undefined {
  const index = config.actions?.findIndex((action) => action.id === id) ?? -1;
  return index < 0 ? undefined : `actions[${index}]`;
}
