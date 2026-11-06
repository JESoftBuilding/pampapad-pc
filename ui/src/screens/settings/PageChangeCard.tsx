import { useId } from "react";
import type { Config } from "@/bridge/contract";
import { setModuleDoubleTap, setModuleLayer } from "@/model/draft";
import { Field } from "@/ui/field";
import { Select } from "@/ui/select";
import { inLibrary, SettingsCard } from "./SettingsCard";

/**
 * Cómo se cambia de página (05 §4.5): la perilla, moduleKey.layer, la página que se ve mientras se mantiene la tecla del módulo, y
 * moduleKey.doubleTap, la acción del doble toque. Quitar una página que la nombra lo sigue frenando el validador (02 §10, problema 4).
 */
export function PageChangeCard({ config, setDraft }: { config: Config; setDraft: (change: (current: Config) => Config) => void }) {
  const id = useId();
  const doubleTapId = useId();
  const doubleTap = config.moduleKey?.doubleTap ?? "";
  const actions = (config.actions ?? []).filter((action) => action.id !== undefined);
  const missingAction = doubleTap !== "" && !actions.some((action) => action.id === doubleTap);
  const layer = config.moduleKey?.layer ?? "";
  const pages = (config.pages ?? []).filter((page) => page.id !== undefined);
  const missing = layer !== "" && !pages.some((page) => page.id === layer);
  return (
    <SettingsCard id="cambio-de-pagina" title="Cómo se cambia de página" place={inLibrary}>
      <p className="text-sm">Con la perilla: recorre las que tienen Mostrar.</p>
      <Field label="Página que se ve mientras se mantiene la tecla del módulo" htmlFor={id}>
        <Select id={id} value={layer} onChange={(event) => setDraft((current) => setModuleLayer(current, event.target.value === "" ? null : event.target.value))}>
          <option value="">Sin asignar</option>
          {missing && <option value={layer}>{layer} (no existe)</option>}
          {pages.map((page) => (
            <option key={page.id} value={page.id}>
              {page.name ?? page.id}
            </option>
          ))}
        </Select>
      </Field>
      <p className="text-xs text-muted">
        Con la tecla del módulo mantenida, cada tecla corre su acción de esa página; para ir a otra página, esa acción lleva el paso Ir a página.
      </p>
      <Field label="Doble toque de la tecla del módulo" htmlFor={doubleTapId}>
        <Select id={doubleTapId} value={doubleTap} onChange={(event) => setDraft((current) => setModuleDoubleTap(current, event.target.value === "" ? null : event.target.value))}>
          <option value="">Sin asignar</option>
          {missingAction && <option value={doubleTap}>{doubleTap} (no existe)</option>}
          {actions.map((action) => (
            <option key={action.id} value={action.id}>
              {action.name ?? action.id}
            </option>
          ))}
        </Select>
      </Field>
      <p className="text-xs text-muted">Con un doble toque asignado, el toque solo espera un instante para saber si viene el segundo.</p>
    </SettingsCard>
  );
}
