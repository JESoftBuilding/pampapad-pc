import { useId } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { Condition, ConditionType, Page } from "@/bridge/contract";
import { withValue } from "@/model/steps";
import { Field } from "@/ui/field";
import { Select } from "@/ui/select";
import { Switch } from "@/ui/switch";
import { StepField } from "./StepField";

/** El "sólo si" pegado a un paso (02 §4.5 y §4.6): una condición del catálogo con sus campos y negate. Sin condición, el paso corre siempre. */
export function OnlyIfEditor({ onlyIf, conditionTypes, pages, bridge, onChange }: { onlyIf?: Condition; conditionTypes: ConditionType[]; pages: Page[]; bridge: Bridge; onChange: (onlyIf: Condition | undefined) => void }) {
  const id = useId();
  const type = conditionTypes.find((candidate) => candidate.id === onlyIf?.type);
  return (
    <div className="space-y-3 rounded-md border border-border p-3">
      <Field label="Sólo si" htmlFor={id} hint="Si no se cumple, este paso se saltea y la cadena sigue (decisión 15).">
        <Select id={id} value={onlyIf?.type ?? ""} onChange={(event) => onChange(event.target.value === "" ? undefined : { type: event.target.value })}>
          <option value="">— Siempre —</option>
          {conditionTypes.map((condition) => (
            <option key={condition.id} value={condition.id}>
              {condition.name}
            </option>
          ))}
          {onlyIf?.type !== undefined && type === undefined && <option value={onlyIf.type}>{onlyIf.type} (condición desconocida)</option>}
        </Select>
      </Field>
      {onlyIf !== undefined && (
        <>
          {type?.fields.map((field) => (
            <StepField key={field.name} field={field} value={onlyIf[field.name]} onChange={(value) => onChange(withValue(onlyIf, field.name, value))} bridge={bridge} pages={pages} />
          ))}
          <div className="flex items-center gap-2 text-sm">
            <Switch label="Al revés (negate)" checked={onlyIf.negate === true} onChange={(checked) => onChange(checked ? { ...onlyIf, negate: true } : (withValue(onlyIf, "negate", undefined) as Condition))} />
            <span>Al revés: el paso corre si la condición no se cumple (negate)</span>
          </div>
        </>
      )}
    </div>
  );
}
