// Las páginas que se muestran, en Ajustes (05 §4.5): el orden, la principal, show y el nombre. Cada operación devuelve una copia con lo
// mínimo cambiado, como draft.ts. mainPage es uno solo, así que elegir la principal deja una sola. Puro y sin importar nada.

type PageLike = { id?: string; name?: string; show?: boolean; [extra: string]: unknown };
export type PagesConfigLike = { mainPage?: string; pages?: PageLike[]; [extra: string]: unknown };

/** La principal: mainPage o, si falta, la primera de pages (02 §4.1). */
export function mainPageOf(config: PagesConfigLike): string | undefined {
  return config.mainPage ?? config.pages?.[0]?.id;
}

/** Sube (-1) o baja (+1). Sin mainPage, mover la primera fija la que era principal: si no, la principal cambiaría sin pedirlo. */
export function movePage<C extends PagesConfigLike>(config: C, index: number, delta: -1 | 1): C {
  const pages = config.pages ?? [];
  const to = index + delta;
  if (index < 0 || index >= pages.length || to < 0 || to >= pages.length) {
    return config;
  }

  const next = [...pages];
  [next[index], next[to]] = [next[to], next[index]];
  const main = mainPageOf(config);
  const pinned = config.mainPage === undefined && main !== undefined && next[0]?.id !== main ? { mainPage: main } : {};
  return { ...config, ...pinned, pages: next };
}

export function setMainPage<C extends PagesConfigLike>(config: C, id: string): C {
  return { ...config, mainPage: id };
}

function setPage<C extends PagesConfigLike>(config: C, index: number, edit: (page: PageLike) => PageLike): C {
  return { ...config, pages: (config.pages ?? []).map((page, i) => (i === index ? edit(page) : page)) };
}

/** show en verdadero es el valor por defecto y no se escribe (02 §4.10). */
export function setPageShown<C extends PagesConfigLike>(config: C, index: number, show: boolean): C {
  return setPage(config, index, (page) => {
    const next = { ...page };
    if (show) {
      delete next.show;
    } else {
      next.show = false;
    }

    return next;
  });
}

/** Cambia sólo el nombre: el id no se toca, porque lo nombran mainPage y los page.go. */
export function renamePage<C extends PagesConfigLike>(config: C, index: number, name: string): C {
  return setPage(config, index, (page) => ({ ...page, name }));
}
