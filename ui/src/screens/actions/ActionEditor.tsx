import type { Bridge } from "@/bridge/bridge";
import type { Action, ActionFinished, ConditionType, Page, Problem, Step, StepType } from "@/bridge/contract";
import { usageText, type Usage } from "@/model/usage";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import { Field } from "@/ui/field";
import { Input } from "@/ui/input";
import { StepList } from "./StepList";

type Props = {
  action: Action;
  usage: Usage[];
  problems: Problem[];
  stepTypes: StepType[];
  conditionTypes: ConditionType[];
  pages: Page[];
  bridge: Bridge;
  lastRun?: ActionFinished;
  onRename: (name: string) => void;
  onRemove: () => void;
  onAdd: (type: string) => void;
  onChange: (index: number, step: Step) => void;
  onMove: (index: number, to: number) => void;
  onRemoveStep: (index: number) => void;
};

/** El editor de la acción elegida (05 §4.2): el nombre, dónde se usa, sus pasos y quitarla si nadie la usa. */
export function ActionEditor({ action, usage, problems, stepTypes, conditionTypes, pages, bridge, lastRun, onRename, onRemove, onAdd, onChange, onMove, onRemoveStep }: Props) {
  return (
    <div className="space-y-4">
      <Field label="Nombre" htmlFor="action-name" hint={`id: ${action.id ?? "(sin id)"}. El id se fija al crear y no cambia (02 §3, regla 5).`}>
        <Input id="action-name" value={action.name ?? ""} onChange={(event) => onRename(event.target.value)} />
      </Field>
      <p className="text-sm text-muted">{usage.length === 0 ? "No la usa ninguna tecla ni ningún atajo." : `La usan: ${usage.map(usageText).join(" · ")}`}</p>
      {problems.length > 0 && (
        <ul aria-label="Problemas de la acción" className="space-y-1 text-sm">
          {problems.map((problem) => (
            <li key={`${problem.where}|${problem.code}`}>
              <Badge tone={problem.severity === "error" ? "bad" : "warn"}>{problem.severity === "error" ? "error" : "aviso"}</Badge> <span className="font-mono text-xs text-muted">{problem.where}</span> {problem.message}
            </li>
          ))}
        </ul>
      )}
      <StepList action={action} stepTypes={stepTypes} conditionTypes={conditionTypes} pages={pages} bridge={bridge} lastRun={lastRun} onAdd={onAdd} onChange={onChange} onMove={onMove} onRemove={onRemoveStep} />
      <Button size="sm" variant="ghost" onClick={onRemove} disabled={usage.length > 0} title={usage.length > 0 ? `La usan: ${usage.map(usageText).join(" · ")}` : undefined}>
        Quitar la acción
      </Button>
    </div>
  );
}
