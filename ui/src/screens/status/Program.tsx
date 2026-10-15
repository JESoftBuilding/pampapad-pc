import type { State } from "@/bridge/contract";
import { updateText } from "@/model/status";
import { Button } from "@/ui/button";
import { Card, CardTitle } from "@/ui/card";

/** Programa (05 §4.4): versión, versión del esquema y fase de la actualización, con el motivo si falló (05 §8). */
export function ProgramCard({ version, schemaVersion, update }: { version: string; schemaVersion: number; update: State["update"] }) {
  return (
    <Card>
      <CardTitle>Programa</CardTitle>
      <dl className="grid grid-cols-[10rem_1fr] gap-x-4 gap-y-2 text-sm">
        <dt className="text-muted">Versión</dt>
        <dd>{version}</dd>
        <dt className="text-muted">Esquema de config.json</dt>
        <dd>{schemaVersion}</dd>
        <dt className="text-muted">Actualización</dt>
        <dd>{updateText(update.phase, update.version, update.detail)}</dd>
      </dl>
    </Card>
  );
}

/** Chequeo previo (05 §4.4): depende de Preflight, que es de la etapa 5 (01 §8). Hasta entonces, el lugar y la explicación. */
export function PreflightCard() {
  return (
    <Card>
      <CardTitle>Chequeo previo</CardTitle>
      <p className="text-sm">El chequeo previo llega en la etapa 5: hasta entonces no hay nada que correr.</p>
      <Button size="sm" variant="outline" disabled className="mt-3">
        Correr
      </Button>
    </Card>
  );
}
