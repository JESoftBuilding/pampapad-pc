import { useEffect, useRef, useState } from "react";
import type { ConsoleFrame, PreviewSample } from "@/bridge/contract.console";
import { Card } from "@/ui/card";
import { cn } from "@/ui/utils";
import { drawScreen } from "./draw";
import { HEIGHT, toRgba, WIDTH } from "./gfx";

/** La pantalla de la consola en un canvas de 320 × 240, píxel por píxel; el tamaño en la página lo da className. Sin frame, queda negra. */
export function ConsoleCanvas({ frame, label, className }: { frame?: ConsoleFrame; label: string; className?: string }) {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const context = ref.current?.getContext("2d");
    if (context != null && frame !== undefined) {
      context.putImageData(new ImageData(toRgba(drawScreen(frame).pixels), WIDTH, HEIGHT), 0, 0);
    }
  }, [frame]);
  return <canvas ref={ref} width={WIDTH} height={HEIGHT} role="img" aria-label={label} className={cn("block bg-black", className)} />;
}

const samples: Array<{ id: PreviewSample; label: string }> = [
  { id: "recording", label: "Grabando" },
  { id: "paused", label: "En pausa" },
  { id: "stopped", label: "Detenida" },
  { id: "noObs", label: "Sin OBS" },
];

function Toggle<T extends string | number>({ label, options, value, onChange }: { label: string; options: Array<{ id: T; label: string }>; value: T; onChange: (id: T) => void }) {
  return (
    <div role="group" aria-label={label} className="inline-flex flex-wrap gap-0.5 rounded-md border border-border p-0.5">
      {options.map((option) => (
        <button
          key={option.id}
          type="button"
          aria-pressed={option.id === value}
          onClick={() => onChange(option.id)}
          className={cn("rounded px-3 py-1 text-sm outline-none focus-visible:ring-2 focus-visible:ring-accent/60", option.id === value ? "bg-background text-foreground" : "text-muted hover:text-foreground")}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

/** "Así se ve en la consola" (05 §4.1): la pantalla en Grabando, En pausa, Detenida o Sin OBS, en tamaño real o ×2, con el borrador. */
export function ConsolePreview({ frame, sample, onSample }: { frame?: ConsoleFrame; sample: PreviewSample; onSample: (sample: PreviewSample) => void }) {
  const [zoom, setZoom] = useState(1);
  return (
    <Card className="space-y-3">
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex-1">
          <h2 className="font-display text-sm text-foreground">Así se ve en la consola</h2>
          <p className="text-xs text-muted">320 × 240 píxeles, con la letra de 6 × 8 del firmware. Muestra el borrador, sin guardar.</p>
        </div>
        <Toggle
          label="Tamaño"
          options={[
            { id: 1, label: "×1" },
            { id: 2, label: "×2" },
          ]}
          value={zoom}
          onChange={setZoom}
        />
      </div>
      <Toggle label="Estado de la grabación en la vista previa" options={samples} value={sample} onChange={onSample} />
      <div className="max-w-full overflow-x-auto">
        <ConsoleCanvas
          frame={frame}
          label="La pantalla de la consola en su resolución real"
          className={cn("[image-rendering:pixelated]", zoom === 2 ? "h-[480px] w-[640px]" : "h-[240px] w-[320px]")}
        />
      </div>
    </Card>
  );
}
