import type { Bridge } from "@/bridge/bridge";
import type { ConditionType, Page, Step, StepType } from "@/bridge/contract";
import { lightValue, withOnlyIf, withValue } from "@/model/steps";
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
        type.fields.map((field) => <StepField key={field.name} field={field} value={step[field.name]} onChange={(value) => onChange(withValue(step, field.name, read(type, field.name, value)))} bridge={bridge} pages={pages} stepType={type.id} />)
      )}
      {type?.id === "lights" && <LightsHelp />}
      <OnlyIfEditor onlyIf={step.onlyIf} conditionTypes={conditionTypes} pages={pages} bridge={bridge} onChange={(onlyIf) => onChange(withOnlyIf(step, onlyIf))} />
    </div>
  );
}

// El value de lights es un solo campo de texto: un entero escrito se guarda como número (02 §5).
function read(type: StepType, name: string, value: unknown): unknown {
  return type.id === "lights" && name === "value" && typeof value === "string" ? lightValue(value) : value;
}

/** Lo que vale cada comando de luces (plan de la 8b, decisión 4). La tira no acusa: el paso termina bien aunque esté apagada o lejos. */
function LightsHelp() {
  return (
    <ul className="list-disc space-y-0.5 pl-5 text-xs text-muted">
      <li>power: on, off o toggle.</li>
      <li>color: el color del selector; prende la tira.</li>
      <li>brightness: de 0 a 255. brightness no prende la tira: si está apagada, no se ve nada.</li>
      <li>adjustBrightness: de -127 a 127, sin el 0. Sube o baja el brillo.</li>
      <li>preset: de 0 a 5, los colores: violeta, ámbar, blanco, rojo, verde y azul; de 6 a 9, los efectos: respiración, ciclo de colores, vela y amanecer.</li>
      <li>La tira no avisa si recibió: el paso termina bien con la tira apagada o lejos.</li>
    </ul>
  );
}
