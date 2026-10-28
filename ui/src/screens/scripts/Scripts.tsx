import { useEffect } from "react";
import { countText } from "@/model/scripts";
import type { ScreenProps } from "@/screens/screen";
import { Button } from "@/ui/button";
import { Card } from "@/ui/card";
import { ScriptCard } from "./ScriptCard";
import { ScriptFilters, ScriptList } from "./ScriptList";
import { ScriptTest } from "./ScriptTest";
import { useScriptsModel } from "./useScriptsModel";

/** La pantalla Scripts (05 §4.3): cabecera con el contador, Importar y Abrir carpeta; la lista filtrada en la página; la ficha y Probar. */
export function ScriptsScreen({ bridge, editor, script: ficha }: ScreenProps) {
  const model = useScriptsModel(bridge, ficha);
  const open = ficha.open;
  const { refreshView } = ficha;
  useEffect(() => {
    if (model.scripts !== undefined) {
      refreshView(model.scripts);
    }
  }, [model.scripts, refreshView]);
  // Abrir otro script o importar descartaría la ficha sin avisar: mientras tenga cambios, primero se guarda o se descarta.
  const lockReason = ficha.dirty ? "Guardá o descartá los cambios de la ficha primero." : undefined;
  const testing = model.test.kind === "starting" || (model.test.kind === "running" && model.test.output.end === undefined);
  const removeReason =
    open === undefined
      ? undefined
      : open.view.usedBy.length > 0
        ? `Lo usan: ${open.view.usedBy.map((use) => use.actionName ?? use.action).join(", ")}.`
        : open.view.status === "running" || (testing && model.test.kind !== "idle" && model.test.id === open.view.id)
          ? "Está corriendo."
          : undefined;

  return (
    <div className="space-y-4">
      <Card className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <p className="mr-auto text-sm" aria-label="Contador">
            {model.scripts === undefined ? "Pidiendo los scripts…" : countText(model.scripts)}
          </p>
          <Button variant="outline" disabled={lockReason !== undefined} title={lockReason} onClick={() => void model.importScripts()}>
            Importar
          </Button>
          <Button variant="outline" onClick={() => void model.reveal()}>
            Abrir carpeta
          </Button>
        </div>
        <ScriptFilters model={model} />
        {model.listFailure !== undefined && <p className="text-sm text-danger">No se pudo leer la lista: {model.listFailure.message}</p>}
        {model.actionFailure !== undefined && (
          <p role="alert" className="text-sm text-danger">
            {model.actionFailure.message}
          </p>
        )}
      </Card>
      <div className="grid gap-4 xl:grid-cols-[24rem_1fr]">
        <Card>
          {lockReason !== undefined && <p className="mb-2 text-xs text-muted">{lockReason}</p>}
          <ScriptList scripts={model.shown} selected={open?.view.id} locked={lockReason !== undefined} onSelect={(view) => void ficha.select(view)} />
        </Card>
        <Card className="space-y-4">
          <ScriptCard
            ficha={ficha}
            removeReason={removeReason}
            onSaved={() => void model.refresh()}
            onRemove={() => open !== undefined && void model.remove(open.view.id)}
            onReveal={() => open !== undefined && void model.reveal(open.view.id)}
          />
          {open !== undefined && (
            <ScriptTest
              id={open.view.id}
              test={model.test}
              busy={editor.busyReason !== undefined}
              disabledReason={open.view.status === "noManifest" ? "Sin ficha no se puede probar." : undefined}
              onTest={() => void model.startTest(open.view.id)}
              onCancel={() => void model.cancelTest()}
            />
          )}
        </Card>
      </div>
    </div>
  );
}
