import { useEffect, useId, useState, type ReactNode } from "react";
import type { Bridge } from "@/bridge/bridge";
import type { Field as FieldSpec, Page } from "@/bridge/contract";
import { Field } from "@/ui/field";
import { Input } from "@/ui/input";
import { Select } from "@/ui/select";
import { Switch } from "@/ui/switch";
import { Textarea } from "@/ui/textarea";
import { useOptions, type OptionsName } from "../common/useOptions";

type Props = { field: FieldSpec; value: unknown; onChange: (value: unknown) => void; bridge: Bridge; pages: Page[] };

/** Un campo de un paso o de una condición, según su clase (01 §6, paso 5): un caso por FieldKind. El valor vacío quita el parámetro. */
export function StepField(props: Props) {
  const { field } = props;
  switch (field.kind) {
    case "obsScene":
      return <RefField {...props} list="scenes" />;
    case "obsInput":
      return <RefField {...props} list="inputs" />;
    case "script":
      return <ListField {...props} list="scripts" />;
    case "window":
      return <ListField {...props} list="windows" free />;
    case "page":
      return <PageField {...props} />;
    case "json":
      return <JsonField {...props} />;
    case "boolean":
      // Un booleano opcional (script.run.wait) ausente vale "lo que diga la ficha" (02 §9): tres valores, y el primero quita la clave.
      return field.optional ? (
        <Plain {...props}>
          {(id) => (
            <Select id={id} value={props.value === true ? "true" : props.value === false ? "false" : ""} onChange={(event) => props.onChange(event.target.value === "" ? undefined : event.target.value === "true")}>
              <option value="">Como dice la ficha</option>
              <option value="true">Sí</option>
              <option value="false">No</option>
            </Select>
          )}
        </Plain>
      ) : (
        <Field label={field.label}>
          <Switch label={field.label} checked={props.value === true} onChange={(checked) => props.onChange(checked)} />
        </Field>
      );
    case "option":
      return (
        <Plain {...props}>
          {(id) => (
            <Select id={id} value={typeof props.value === "string" ? props.value : ""} onChange={(event) => props.onChange(event.target.value)}>
              <option value="">—</option>
              {(field.options ?? []).map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </Select>
          )}
        </Plain>
      );
    case "number":
    case "duration":
      return (
        <Plain {...props}>
          {(id) => <Input id={id} type="number" value={typeof props.value === "number" ? props.value : ""} onChange={(event) => props.onChange(event.target.value === "" ? undefined : Number(event.target.value))} />}
        </Plain>
      );
    default:
      return <Plain {...props}>{(id) => <Input id={id} value={typeof props.value === "string" ? props.value : ""} onChange={(event) => props.onChange(event.target.value)} />}</Plain>;
  }
}

function Plain({ field, children }: Props & { children: (id: string) => ReactNode }) {
  const id = useId();
  return (
    <Field label={field.label} htmlFor={id} hint={field.optional ? "Opcional" : undefined}>
      {children(id)}
    </Field>
  );
}

/** Una escena o una entrada de OBS: se guarda { uuid, name } (02 §4.8). Sin OBS, el campo acepta el nombre escrito a mano (05 §4.2). */
function RefField({ field, value, onChange, bridge, list }: Props & { list: OptionsName }) {
  const id = useId();
  const options = useOptions(bridge, list);
  const name = typeof value === "string" ? value : typeof value === "object" && value !== null ? String((value as { name?: unknown }).name ?? "") : "";
  const known = options.items.find((item) => item.name === name);
  const byHand = options.unavailable !== undefined || options.error !== undefined;
  return (
    <Field
      label={field.label}
      htmlFor={id}
      hint={options.unavailable ?? options.error ?? (field.optional ? "Opcional" : undefined)}
      error={!byHand && name !== "" && known === undefined && !options.loading ? `${name} no está en OBS: se guarda igual, por nombre.` : undefined}
    >
      {byHand ? (
        <Input id={id} value={name} onChange={(event) => onChange(event.target.value)} />
      ) : (
        <Select id={id} value={name} onChange={(event) => onChange(event.target.value === "" ? undefined : toRef(options.items.find((item) => item.name === event.target.value) ?? { name: event.target.value }))}>
          <option value="">—</option>
          {known === undefined && name !== "" && <option value={name}>{name} (no está en OBS)</option>}
          {options.items.map((item) => (
            <option key={item.uuid ?? item.name} value={item.name}>
              {item.name}
            </option>
          ))}
        </Select>
      )}
    </Field>
  );
}

function toRef(item: { name: string; uuid?: string }): unknown {
  return item.uuid === undefined ? { name: item.name } : { uuid: item.uuid, name: item.name };
}

/** Un script (por id) o una ventana (por título): la lista de options.list y, si está vacía o free, el texto escrito a mano. */
function ListField({ field, value, onChange, bridge, list, free = false }: Props & { list: OptionsName; free?: boolean }) {
  const id = useId();
  const options = useOptions(bridge, list);
  const text = typeof value === "string" ? value : "";
  const listId = `${id}-list`;
  if (free || options.items.length === 0) {
    return (
      <Field label={field.label} htmlFor={id} hint={options.unavailable ?? (field.optional ? "Opcional" : undefined)}>
        <Input id={id} list={listId} value={text} onChange={(event) => onChange(event.target.value)} />
        <datalist id={listId}>
          {options.items.map((item) => (
            <option key={`${item.name}|${item.detail ?? ""}`} value={item.name}>
              {item.detail}
            </option>
          ))}
        </datalist>
      </Field>
    );
  }

  return (
    <Field label={field.label} htmlFor={id} hint={field.optional ? "Opcional" : undefined}>
      <Select id={id} value={text} onChange={(event) => onChange(event.target.value)}>
        <option value="">—</option>
        {text !== "" && !options.items.some((item) => item.name === text) && <option value={text}>{text} (no está en la biblioteca)</option>}
        {options.items.map((item) => (
          <option key={item.name} value={item.name}>
            {item.detail === undefined ? item.name : `${item.detail} (${item.name})`}
          </option>
        ))}
      </Select>
    </Field>
  );
}

/** El destino de page.go: una página de teclas, o next, previous o main (02 §5). */
function PageField({ field, value, onChange, pages }: Props) {
  const id = useId();
  const text = typeof value === "string" ? value : "";
  const ids = pages.filter((page) => page.id !== undefined && (page.type === undefined || page.type === "keys")).map((page) => page.id!);
  return (
    <Field label={field.label} htmlFor={id}>
      <Select id={id} value={text} onChange={(event) => onChange(event.target.value)}>
        <option value="">—</option>
        {text !== "" && !ids.includes(text) && !["next", "previous", "main"].includes(text) && <option value={text}>{text} (no es una página de teclas)</option>}
        {ids.map((pageId) => (
          <option key={pageId} value={pageId}>
            {pageId}
          </option>
        ))}
        <option value="main">main (la principal)</option>
        <option value="next">next (la siguiente)</option>
        <option value="previous">previous (la anterior)</option>
      </Select>
    </Field>
  );
}

/** El requestData de obs.request: un objeto JSON que va tal cual. Se guarda al salir del campo, si es JSON. */
function JsonField({ field, value, onChange }: Props) {
  const id = useId();
  const [text, setText] = useState(value === undefined ? "" : JSON.stringify(value, null, 2));
  const [error, setError] = useState<string>();
  useEffect(() => {
    setText(value === undefined ? "" : JSON.stringify(value, null, 2));
  }, [value]);
  const commit = () => {
    if (text.trim() === "") {
      setError(undefined);
      onChange(undefined);
      return;
    }

    try {
      onChange(JSON.parse(text) as unknown);
      setError(undefined);
    } catch (problem) {
      setError(`No es JSON: ${problem instanceof Error ? problem.message : String(problem)}`);
    }
  };
  return (
    <Field label={field.label} htmlFor={id} hint={field.optional ? "Opcional" : undefined} error={error}>
      <Textarea id={id} value={text} onChange={(event) => setText(event.target.value)} onBlur={commit} />
    </Field>
  );
}
