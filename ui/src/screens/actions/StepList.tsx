import { useState } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { Action, ActionFinished, ConditionType, Page, Step, StepType } from "@/bridge/contract";
import { summary } from "@/model/steps";
import { Badge } from "@/ui/badge";
import { Button } from "@/ui/button";
import { Select } from "@/ui/select";
import { cn } from "@/ui/utils";
import { outcomeText, outcomeTone } from "../common/RunResult";
import { StepForm } from "./StepForm";

type Props = {
  action: Action;
  stepTypes: StepType[];
  conditionTypes: ConditionType[];
  pages: Page[];
  bridge: Bridge;
  lastRun?: ActionFinished;
  onAdd: (type: string) => void;
  onChange: (index: number, step: Step) => void;
  onMove: (index: number, to: number) => void;
  onRemove: (index: number) => void;
};

/** Los pasos, numerados, con su resumen, su condición y el resultado de la última prueba; subir, bajar y quitar llevan la condición con el paso (05 §4.2). */
export function StepList({ action, stepTypes, conditionTypes, pages, bridge, lastRun, onAdd, onChange, onMove, onRemove }: Props) {
  const steps = action.steps ?? [];
  const [open, setOpen] = useState<number>();
  const [adding, setAdding] = useState(stepTypes[0]?.id ?? "");
  const add = () => {
    onAdd(adding);
    setOpen(steps.length);
  };
  // El formulario abierto sigue al paso que se mueve o se quita, no a la posición.
  const move = (index: number, to: number) => {
    onMove(index, to);
    setOpen(open === index ? to : open === to ? index : open);
  };
  const remove = (index: number) => {
    onRemove(index);
    setOpen(open === undefined || open === index ? undefined : open > index ? open - 1 : open);
  };
  return (
    <div className="space-y-3">
      <ol aria-label="Pasos" className="space-y-2">
        {steps.map((step, index) => {
          const type = stepTypes.find((candidate) => candidate.id === step.type);
          const report = lastRun !== undefined && lastRun.actionId === action.id ? lastRun.steps[index] : undefined;
          return (
            <li key={index} className={cn("rounded-md border p-3", type === undefined ? "border-danger border-dashed" : "border-border")}>
              <div className="flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  className="mr-auto text-left text-sm outline-none focus-visible:ring-2 focus-visible:ring-accent/60"
                  aria-expanded={open === index}
                  onClick={() => setOpen(open === index ? undefined : index)}
                >
                  <span className="font-display text-muted">{index + 1}.</span> {summary(step, stepTypes)}
                </button>
                {report !== undefined && <Badge tone={outcomeTone[report.outcome]}>{outcomeText[report.outcome]}</Badge>}
                <Button size="sm" variant="ghost" aria-label={`Subir el paso ${index + 1}`} disabled={index === 0} onClick={() => move(index, index - 1)}>
                  ↑
                </Button>
                <Button size="sm" variant="ghost" aria-label={`Bajar el paso ${index + 1}`} disabled={index === steps.length - 1} onClick={() => move(index, index + 1)}>
                  ↓
                </Button>
                <Button size="sm" variant="ghost" aria-label={`Quitar el paso ${index + 1}`} onClick={() => remove(index)}>
                  Quitar
                </Button>
              </div>
              {open === index && (
                <div className="mt-3">
                  <StepForm step={step} type={type} conditionTypes={conditionTypes} pages={pages} bridge={bridge} onChange={(next) => onChange(index, next)} />
                </div>
              )}
            </li>
          );
        })}
      </ol>
      {steps.length === 0 && <p className="text-sm text-muted">Sin pasos todavía: una acción sin pasos no hace nada.</p>}
      <div className="flex flex-wrap items-end gap-2">
        <label className="text-xs uppercase tracking-wide text-muted" htmlFor="step-type">
          Tipo de paso
        </label>
        <Select id="step-type" className="w-64" value={adding} onChange={(event) => setAdding(event.target.value)}>
          {stepTypes.map((type) => (
            <option key={type.id} value={type.id}>
              {type.name} ({type.id})
            </option>
          ))}
        </Select>
        <Button size="sm" variant="outline" onClick={add} disabled={adding === ""}>
          Agregar paso
        </Button>
      </div>
    </div>
  );
}
