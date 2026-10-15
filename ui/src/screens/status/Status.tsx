import type { ScreenProps } from "@/screens/screen";
import { Card } from "@/ui/card";
import { Connections } from "./Connections";
import { LogView } from "./LogView";
import { Problems } from "./Problems";
import { PreflightCard, ProgramCard } from "./Program";
import { Running } from "./Running";
import { useStatusModel } from "./useStatusModel";

/** La pantalla Estado (05 §4.4). Sólo lee, salvo Cancelar. Todo es Propuesto y no tuvo boceto (pregunta abierta 37). */
export function StatusScreen({ bridge, view }: ScreenProps) {
  const model = useStatusModel(bridge);
  if (view.state === undefined || view.snapshot === undefined) {
    return (
      <Card>
        <p>{view.failure === undefined ? "Pidiendo el estado al programa…" : `No se pudo leer el estado del programa: ${view.failure.message}`}</p>
      </Card>
    );
  }

  const { state, snapshot } = view;
  return (
    <div className="space-y-4">
      {model.notice !== undefined && (
        <p role="status" className="text-sm text-accent">
          {model.notice}
        </p>
      )}
      <div className="grid gap-4 xl:grid-cols-2">
        <Connections state={state} settings={snapshot.settings} />
        <Running running={state.running} finished={model.finished} onCancel={(runId) => void model.cancel(runId)} />
        <Problems problems={state.problems} library={state.library} />
        <div className="grid gap-4">
          <ProgramCard version={snapshot.version} schemaVersion={snapshot.schemaVersion} update={state.update} />
          <PreflightCard />
        </div>
        <LogView entries={model.entries} filter={model.filter} onFilter={model.setFilter} />
      </div>
    </div>
  );
}
