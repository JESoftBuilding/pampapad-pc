import type { Bridge } from "@/bridge/bridge";
import type { ConditionType, Page, Step, StepType } from "@/bridge/contract";
import { withOnlyIf, withValue } from "@/model/steps";
import { Textarea } from "@/ui/textarea";
import { OnlyIfEditor } from "./OnlyIfEditor";
import { StepField } from "./StepField";

/** El formulario de un paso: sale de los Field de su tipo (05 §4.2). Un tipo que esta versión no conoce se conserva tal cual y se muestra roto. */
export function StepForm({ step, type, conditionTypes, pages, bridge, onChange }: { step: Step; type?: StepType; conditionTypes: ConditionType[]; pages: Page[]; bridge: Bridge; onChange: (step: Step) => void }) {
  return (
    <div className="space-y-3">
      {type === undefined ? (
        <div className="space-y-2">
          <p className="text-sm text-accent">Este programa no conoce el tipo {step.type ?? "(sin tipo)"}: el paso se conserva tal cual al guardar (02 §4.5) y no corre.</p>
          <Textarea aria-label="El paso, tal cual está en config.json" readOnly value={JSON.stringify(step, null, 2)} />
        </div>
      ) : (
        type.fields.map((field) => <StepField key={field.name} field={field} value={step[field.name]} onChange={(value) => onChange(withValue(step, field.name, value))} bridge={bridge} pages={pages} />)
      )}
      <OnlyIfEditor onlyIf={step.onlyIf} conditionTypes={conditionTypes} pages={pages} bridge={bridge} onChange={(onlyIf) => onChange(withOnlyIf(step, onlyIf))} />
    </div>
  );
}
