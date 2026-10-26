import { useCallback, useEffect, useState } from "react";
import type { Action, Step } from "@/bridge/contract";
import { moveStep, newActionId, removeAction, removeStep, setStep, upsertAction } from "@/model/draft";
import { problemsUnder } from "@/model/steps";
import { usageOf, type Usage } from "@/model/usage";
import { queryOf, whereOfAction } from "@/model/where";
import type { Editor } from "../common/useDraft";

/**
 * Lo que Acciones junta además de la foto (05 §4.2): la acción elegida, quién la usa, sus problemas y los cambios de sus pasos.
 * El borrador (editor) llega de App, que lo llama una sola vez: pasar de Acciones a Teclas no lo descarta.
 */
export function useActionsModel(editor: Editor) {
  const [selectedId, setSelectedId] = useState<string | undefined>(() => queryOf(window.location.hash).get("id") ?? undefined);

  // Un enlace de Estado (05 §4.4) llega por el hash: #/actions?id=ir-al-banco.
  useEffect(() => {
    const onHash = () => setSelectedId(queryOf(window.location.hash).get("id") ?? undefined);
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  const actions = editor.draft?.actions ?? [];
  const action = actions.find((candidate) => candidate.id === selectedId);
  const draft = editor.draft;
  const loaded = editor.loaded;

  const usageFor = useCallback((id: string): Usage[] => (draft === undefined ? [] : usageOf(draft, id)), [draft]);

  // Los problemas vienen con los índices del archivo cargado, no del borrador: se buscan ahí.
  const problemsFor = useCallback(
    (id: string) => {
      const where = loaded === undefined ? undefined : whereOfAction(loaded, id);
      return where === undefined ? [] : problemsUnder(editor.problems, where);
    },
    [editor.problems, loaded],
  );

  const create = useCallback(
    (name: string) => {
      if (draft === undefined) {
        return;
      }

      const id = newActionId(draft, name);
      editor.setDraft((current) => upsertAction(current, { id, name: name.trim().length === 0 ? id : name.trim(), steps: [] }));
      setSelectedId(id);
    },
    [draft, editor],
  );

  const change = useCallback(
    (update: (current: Action) => Action) => {
      if (selectedId !== undefined) {
        editor.setDraft((current) => {
          const found = (current.actions ?? []).find((candidate) => candidate.id === selectedId);
          return found === undefined ? current : upsertAction(current, update(found));
        });
      }
    },
    [editor, selectedId],
  );

  const remove = useCallback(() => {
    if (selectedId !== undefined && usageFor(selectedId).length === 0) {
      editor.setDraft((current) => removeAction(current, selectedId));
      setSelectedId(undefined);
    }
  }, [editor, selectedId, usageFor]);

  return {
    actions,
    action,
    usageFor,
    problemsFor,
    select: (id?: string) => setSelectedId(id),
    create,
    rename: (name: string) => change((current) => ({ ...current, name })),
    remove,
    addStep: (type: string) => change((current) => ({ ...current, steps: [...(current.steps ?? []), { type }] })),
    updateStep: (index: number, step: Step) => change((current) => setStep(current, index, step)),
    move: (index: number, to: number) => change((current) => moveStep(current, index, to)),
    removeStepAt: (index: number) => change((current) => removeStep(current, index)),
  };
}
