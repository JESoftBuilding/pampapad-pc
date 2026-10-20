import { Card, CardTitle } from "@/ui/card";

/** Una pantalla que todavía no existe: dice en qué etapa llega (01 §8) y qué se hace mientras tanto. */
export function Placeholder({ title, stage, note }: { title: string; stage: number; note: string }) {
  return (
    <Card className="max-w-xl">
      <CardTitle>{title}</CardTitle>
      <p>Esta pantalla llega en la etapa {stage}.</p>
      <p className="mt-2 text-sm text-muted">{note}</p>
    </Card>
  );
}
