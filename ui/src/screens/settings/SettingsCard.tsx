import type { ReactNode } from "react";
import { Card, CardTitle } from "@/ui/card";

/** Una tarjeta de Ajustes (05 §4.5): dice arriba dónde se guarda lo que tiene. */
export function SettingsCard({ id, title, place, children }: { id: string; title: string; place: string; children: ReactNode }) {
  return (
    <Card id={id} aria-labelledby={`${id}-title`} className="space-y-3">
      <div className="flex flex-wrap items-baseline gap-2">
        <CardTitle id={`${id}-title`} className="mr-auto">
          {title}
        </CardTitle>
        <span className="text-xs text-muted">{place}</span>
      </div>
      {children}
    </Card>
  );
}

export const inLibrary = "Biblioteca (se versiona)";
export const onlyThisPc = "Sólo esta PC";
