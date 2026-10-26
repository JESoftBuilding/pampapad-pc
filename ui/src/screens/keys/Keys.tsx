import type { ScreenProps } from "@/screens/screen";
import { Card } from "@/ui/card";
import { RunResult } from "../common/RunResult";
import { SaveBar } from "../common/SaveBar";
import { KeyEditor } from "./KeyEditor";
import { KeyGrid } from "./KeyGrid";
import { PageSelector } from "./PageSelector";
import { useKeysModel } from "./useKeysModel";

/** La pantalla Teclas (05 §4.1): la página elegida, sus doce teclas y el editor de la que se toca. Guardar manda config.json entero. */
export function KeysScreen({ bridge, view, editor }: ScreenProps) {
  const model = useKeysModel(bridge, editor);
  if (view.snapshot === undefined || editor.draft === undefined) {
    return (
      <Card>
        <p>{view.failure === undefined ? "Pidiendo la configuración al programa…" : `No se pudo leer la configuración: ${view.failure.message}`}</p>
      </Card>
    );
  }

  return (
    <div className="space-y-4" data-fingerprint={view.snapshot.fingerprint}>
      <PageSelector pages={model.pages} current={model.pageId} mainPage={model.mainPage} onSelect={model.selectPage} />
      <div className="grid gap-4 xl:grid-cols-[auto_1fr]">
        <Card>
          <KeyGrid page={model.page} selected={model.position} problemsOf={model.problemsOf} onSelect={model.selectKey} />
          <p className="mt-3 text-xs text-muted">La consola dibujada y la pantalla de 320 × 240 llegan con la etapa 9 (pregunta 7 de este plan).</p>
        </Card>
        <Card>
          {model.page === undefined || model.position === undefined ? (
            <p className="text-sm text-muted">Elegí una tecla.</p>
          ) : (
            <KeyEditor
              page={model.page}
              position={model.position}
              current={model.key}
              actions={model.actions}
              scripts={model.scripts}
              problems={model.problemsOf(model.position)}
              bridge={bridge}
              busyReason={editor.busyReason}
              takenBy={model.takenBy}
              onChange={model.update}
              onScript={model.chooseScript}
              onRemove={model.removeKey}
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
