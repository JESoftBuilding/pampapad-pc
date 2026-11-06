import { useState } from "react";
import type { PreviewSample } from "@/bridge/contract.console";
import { ConsolePreview } from "@/console/ConsolePreview";
import { useConsolePreview } from "@/console/useConsolePreview";
import { setPageKnob } from "@/model/draft";
import type { ScreenProps } from "@/screens/screen";
import { Card } from "@/ui/card";
import { RunResult } from "../common/RunResult";
import { ConsoleBody, PieceInfo, type Piece } from "./ConsoleBody";
import { SaveBar } from "../common/SaveBar";
import { KeyEditor } from "./KeyEditor";
import { KeyGrid } from "./KeyGrid";
import { PageSelector } from "./PageSelector";
import { useKeysModel } from "./useKeysModel";

/**
 * La pantalla Teclas (05 §4.1): la página elegida, la consola dibujada con sus doce teclas, el editor de la pieza que se toca y cómo se ve
 * la pantalla de la consola con el borrador. Guardar manda config.json entero.
 */
export function KeysScreen({ bridge, view, editor }: ScreenProps) {
  const model = useKeysModel(bridge, editor);
  const [sample, setSample] = useState<PreviewSample>("recording");
  const [piece, setPiece] = useState<Piece>();
  const frame = useConsolePreview(bridge, editor.draft, model.pageId, sample);
  if (view.snapshot === undefined || editor.draft === undefined) {
    return (
      <Card>
        <p>{view.failure === undefined ? "Pidiendo la configuración al programa…" : `No se pudo leer la configuración: ${view.failure.message}`}</p>
      </Card>
    );
  }

  // Con la perilla o la tecla del módulo elegidas, Probar no corre la acción de la última tecla.
  const action = piece === undefined ? model.action : undefined;
  const layer = editor.draft.moduleKey?.layer;
  const layerName = layer === undefined ? undefined : (model.pages.find((candidate) => candidate.id === layer)?.name ?? layer);
  const doubleTap = editor.draft.moduleKey?.doubleTap;
  const doubleTapName = doubleTap === undefined ? undefined : (model.actions.find((candidate) => candidate.id === doubleTap)?.name ?? `${doubleTap} (no existe)`);
  const pageId = model.page?.id;
  return (
    <div className="space-y-4" data-fingerprint={view.snapshot.fingerprint}>
      <PageSelector pages={model.pages} current={model.pageId} mainPage={model.mainPage} onSelect={model.selectPage} />
      <div className="grid gap-4 xl:grid-cols-[auto_1fr]">
        <div className="space-y-4">
          <Card>
            <ConsoleBody frame={frame} piece={piece} onPiece={setPiece}>
              <KeyGrid
                page={model.page}
                selected={piece === undefined ? model.position : undefined}
                problemsOf={model.problemsOf}
                onSelect={(position) => {
                  setPiece(undefined);
                  model.selectKey(position);
                }}
              />
            </ConsoleBody>
            <p className="mt-3 text-xs text-muted">Elegí una tecla, la perilla o la tecla del módulo.</p>
          </Card>
          <ConsolePreview frame={frame} sample={sample} onSample={setSample} />
        </div>
        <Card>
          {piece !== undefined ? (
            <PieceInfo
              piece={piece}
              layerName={layerName}
              doubleTapName={doubleTapName}
              knob={model.page?.knob}
              onKnob={(knob) => pageId !== undefined && editor.setDraft((current) => setPageKnob(current, pageId, knob))}
            />
          ) : model.page === undefined || model.position === undefined ? (
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
              action={action}
              run={editor.run}
              busyReason={editor.busyReason}
              stepTypes={view.snapshot.stepTypes}
              onTest={() => action !== undefined && void editor.test(action)}
              onCancel={() => void editor.cancelTest()}
            />
          </div>
        </Card>
      </div>
      <SaveBar dirty={editor.dirty} outdated={editor.outdated} state={editor.saveState} onSave={() => void editor.save()} onDiscard={editor.discard} onReload={() => void editor.reload()} />
    </div>
  );
}
