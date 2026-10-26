import { useCallback, useEffect, useMemo, useState } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { Key, Page } from "@/bridge/contract";
import { keyAt, scriptAction, setKey, slug, upsertAction, type KeyPatch } from "@/model/draft";
import { usedBy } from "@/model/hotkeys";
import { problemsUnder } from "@/model/steps";
import { queryOf, whereOfKey } from "@/model/where";
import type { Editor } from "../common/useDraft";
import { useOptions } from "../common/useOptions";

type Selection = { pageId?: string; position?: number };

function fromHash(): Selection {
  const query = queryOf(window.location.hash);
  const position = Number(query.get("position"));
  return { pageId: query.get("page") ?? undefined, position: position >= 1 && position <= 12 ? position : undefined };
}

/**
 * Lo que Teclas junta además de la foto (05 §4.1): la página y la tecla elegidas, sus problemas, los scripts y los cambios de la tecla.
 * El borrador (editor) llega de App, que lo llama una sola vez: pasar de Teclas a Acciones no lo descarta.
 */
export function useKeysModel(bridge: Bridge, editor: Editor) {
  const scripts = useOptions(bridge, "scripts");
  const [selection, setSelection] = useState<Selection>(fromHash);

  // Un enlace de Estado (05 §4.4) llega por el hash: #/keys?page=grabacion&position=5.
  useEffect(() => {
    const onHash = () => setSelection(fromHash());
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  const pages = useMemo(() => editor.draft?.pages ?? [], [editor.draft]);
  const pageId = selection.pageId ?? editor.draft?.mainPage ?? pages[0]?.id;
  const page: Page | undefined = pages.find((candidate) => candidate.id === pageId);
  const position = selection.position;
  const key: Key | undefined = page !== undefined && position !== undefined ? keyAt(page, position) : undefined;
  const actions = useMemo(() => editor.draft?.actions ?? [], [editor.draft]);
  const action = key?.action === undefined ? undefined : actions.find((candidate) => candidate.id === key.action);

  // Los problemas vienen con los índices del archivo cargado, no del borrador: se buscan ahí.
  const problemsOf = useCallback(
    (at: number) => {
      const where = editor.loaded === undefined || pageId === undefined ? undefined : whereOfKey(editor.loaded, pageId, at);
      return where === undefined ? [] : problemsUnder(editor.problems, where);
    },
    [editor.loaded, editor.problems, pageId],
  );

  const update = useCallback(
    (patch: KeyPatch) => {
      if (pageId !== undefined && position !== undefined) {
        editor.setDraft((current) => setKey(current, pageId, position, patch));
      }
    },
    [editor, pageId, position],
  );

  // Elegir un script deja en la tecla una acción de un paso script.run (05 §4.1): se crea una vez, con el id script-<id>.
  const chooseScript = useCallback(
    (scriptId: string, name: string) => {
      if (pageId === undefined || position === undefined) {
        return;
      }

      const id = slug(`script-${scriptId}`);
      editor.setDraft((current) => {
        const withAction = (current.actions ?? []).some((candidate) => candidate.id === id) ? current : upsertAction(current, scriptAction(id, scriptId, name));
        return setKey(withAction, pageId, position, { action: id });
      });
    },
    [editor, pageId, position],
  );

  const removeKey = useCallback(() => {
    if (pageId !== undefined && position !== undefined) {
      editor.setDraft((current) => ({
        ...current,
        pages: (current.pages ?? []).map((candidate) => (candidate.id === pageId ? { ...candidate, keys: (candidate.keys ?? []).filter((item) => item.position !== position) } : candidate)),
      }));
    }
  }, [editor, pageId, position]);

  // Dónde ya se usa una combinación (05 §6): otra tecla de la página o un atajo general; la que se edita no cuenta contra sí misma.
  const takenBy = useCallback(
    (text: string) => (editor.draft === undefined || pageId === undefined || position === undefined ? null : usedBy(editor.draft, pageId, position, text)),
    [editor.draft, pageId, position],
  );

  return {
    scripts,
    pages,
    pageId,
    page,
    position,
    key,
    actions,
    action,
    mainPage: editor.draft?.mainPage ?? pages[0]?.id,
    problemsOf,
    selectPage: (id: string) => setSelection({ pageId: id, position: undefined }),
    selectKey: (at: number) => setSelection({ pageId, position: at }),
    update,
    chooseScript,
    removeKey,
    takenBy,
  };
}
