import { useId } from "react";
import type { Config } from "@/bridge/contract";
import { setModuleLayer } from "@/model/draft";
import { Field } from "@/ui/field";
import { Select } from "@/ui/select";
import { inLibrary, SettingsCard } from "./SettingsCard";

/**
 * Cómo se cambia de página (05 §4.5): la perilla, que es fija, y moduleKey.layer, la página que se ve mientras se mantiene la tecla del
 * módulo. Quitar una página que la nombra lo sigue frenando el validador (02 §10, problema 4).
 */
export function PageChangeCard({ config, setDraft }: { config: Config; setDraft: (change: (current: Config) => Config) => void }) {
  const id = useId();
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
    </SettingsCard>
  );
}
