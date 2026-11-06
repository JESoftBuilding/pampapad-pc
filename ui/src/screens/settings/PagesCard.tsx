import type { Config } from "@/bridge/contract";
import { useId } from "react";
import { mainPageOf, movePage, renamePage, setMainPage, setMainPageWithoutObs, setPageShown } from "@/model/pages";
import { Button } from "@/ui/button";
import { Field } from "@/ui/field";
import { Input } from "@/ui/input";
import { Select } from "@/ui/select";
import { Switch } from "@/ui/switch";
import { inLibrary, SettingsCard } from "./SettingsCard";

/**
 * Páginas que se muestran (05 §4.5): el orden es el recorrido de la perilla; Mostrar (show), la Principal (mainPage), el nombre y la
 * principal sin OBS (mainPageWithoutObs, 02 §7, regla 9).
 */
export function PagesCard({ config, setDraft }: { config: Config; setDraft: (change: (current: Config) => Config) => void }) {
  const pages = config.pages ?? [];
  const main = mainPageOf(config);
  const withoutObsId = useId();
  // Sólo las de teclas pueden ser la base (02 §4.2): las demás serían el problema 4.
  const keysPages = pages.filter((page) => page.id !== undefined && (page.type === undefined || page.type === "keys"));
  // Un valor escrito a mano que no está en la lista se sigue viendo, en vez de pasar por "la misma".
  const withoutObs = config.mainPageWithoutObs;
  const stray = withoutObs !== undefined && !keysPages.some((page) => page.id === withoutObs) ? withoutObs : undefined;
  return (
    <SettingsCard id="paginas" title="Páginas que se muestran" place={inLibrary}>
      <ol aria-label="Páginas" className="space-y-2">
        {pages.map((page, index) => {
          const name = page.name ?? page.id ?? `página ${index + 1}`;
          return (
            <li key={page.id ?? index} className="flex flex-wrap items-center gap-2 text-sm">
              <Input aria-label={`Nombre de ${page.id ?? index + 1}`} className="w-48" value={page.name ?? ""} onChange={(event) => setDraft((current) => renamePage(current, index, event.target.value))} />
              <span className="font-mono text-xs text-muted">{page.id}</span>
              <Button size="sm" variant="ghost" aria-label={`Subir ${name}`} disabled={index === 0} onClick={() => setDraft((current) => movePage(current, index, -1))}>
                Subir
              </Button>
              <Button size="sm" variant="ghost" aria-label={`Bajar ${name}`} disabled={index === pages.length - 1} onClick={() => setDraft((current) => movePage(current, index, 1))}>
                Bajar
              </Button>
              <Switch label={`Mostrar ${name}`} checked={page.show !== false} onChange={(show) => setDraft((current) => setPageShown(current, index, show))} /> Mostrar
              <label className="flex items-center gap-1">
                <input type="radio" name="main-page" checked={page.id === main} disabled={page.id === undefined} onChange={() => page.id !== undefined && setDraft((current) => setMainPage(current, page.id ?? ""))} />
                Principal
              </label>
            </li>
          );
        })}
      </ol>
      <Field label="Principal sin OBS" htmlFor={withoutObsId}>
        <Select
          id={withoutObsId}
          value={withoutObs ?? ""}
          onChange={(event) => setDraft((current) => setMainPageWithoutObs(current, event.target.value === "" ? undefined : event.target.value))}
        >
          <option value="">La misma que la principal</option>
          {keysPages.map((page) => (
            <option key={page.id} value={page.id}>
              {page.name ?? page.id}
            </option>
          ))}
          {stray !== undefined && <option value={stray}>{stray}</option>}
        </Select>
      </Field>
      <p className="text-xs text-muted">
        Mostrar tiene efecto con la perilla de la consola (etapa 9); la principal es la de arranque y la que queda de base mientras se graba. Con OBS cerrado rige la principal sin OBS.
      </p>
    </SettingsCard>
  );
}
