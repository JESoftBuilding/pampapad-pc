import type { ScreenProps } from "@/screens/screen";
import { Card } from "@/ui/card";
import { RunResult } from "../common/RunResult";
import { SaveBar } from "../common/SaveBar";
import { ActionEditor } from "./ActionEditor";
import { ActionList } from "./ActionList";
import { useActionsModel } from "./useActionsModel";

/** La pantalla Acciones (05 §4.2): la lista, el editor de la elegida con sus pasos, Probar y Guardar. Guardar manda config.json entero. */
export function ActionsScreen({ bridge, view, editor }: ScreenProps) {
  const model = useActionsModel(editor);
  if (view.snapshot === undefined || editor.draft === undefined) {
    return (
      <Card>
        <p>{view.failure === undefined ? "Pidiendo la configuración al programa…" : `No se pudo leer la configuración: ${view.failure.message}`}</p>
      </Card>
    );
  }

  const lastRun = editor.run.kind === "finished" ? editor.run.result : undefined;
  return (
    <div className="space-y-4">
      <div className="grid gap-4 xl:grid-cols-[20rem_1fr]">
        <Card>
          <ActionList actions={model.actions} selected={model.action?.id} usageFor={model.usageFor} problemsFor={model.problemsFor} onSelect={model.select} onCreate={model.create} />
        </Card>
        <Card>
          {model.action === undefined ? (
            <p className="text-sm text-muted">Elegí una acción, o creá una nueva.</p>
          ) : (
            <ActionEditor
              action={model.action}
              usage={model.usageFor(model.action.id ?? "")}
              problems={model.problemsFor(model.action.id ?? "")}
              stepTypes={view.snapshot.stepTypes}
              conditionTypes={view.snapshot.conditionTypes}
              pages={editor.draft.pages ?? []}
              bridge={bridge}
              lastRun={lastRun}
              onRename={model.rename}
              onRemove={model.remove}
              onAdd={model.addStep}
              onChange={model.updateStep}
              onMove={model.move}
              onRemoveStep={model.removeStepAt}
            />
          )}
          <div className="mt-4">
            <RunResult
              action={model.action}
              run={editor.run}
              busyReason={editor.busyReason}
              stepTypes={view.snapshot.stepTypes}
              onTest={() => model.action !== undefined && void editor.test(model.action)}
              onCancel={() => void editor.cancelTest()}
            />
          </div>
        </Card>
      </div>
      <SaveBar dirty={editor.dirty} outdated={editor.outdated} state={editor.saveState} onSave={() => void editor.save()} onDiscard={editor.discard} onReload={() => void editor.reload()} />
    </div>
  );
}
