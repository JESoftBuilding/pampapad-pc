import type { ScreenProps } from "@/screens/screen";
import { Card } from "@/ui/card";

/** Provisorio: la pantalla Estado entera llega en E6-11. */
export function StatusScreen({ view }: ScreenProps) {
  return (
    <Card>
      <p>{view.state === undefined ? "Pidiendo el estado al programa…" : `Estado número ${view.state.seq}`}</p>
    </Card>
  );
}
