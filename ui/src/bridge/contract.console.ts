import { z } from "zod";

// Lo que contesta console.preview (plan de la 9b, decisión 13): espejo de src/PampaPad.Core/Console/ConsoleFrame.cs y ScreenFrame.cs,
// aparte de contract.ts por el tope de 300 líneas. Es lo que saldría por el cable; las banderas van en número, como en el cable.

export const previewSampleSchema = z.enum(["recording", "paused", "stopped", "noObs"]);

const consoleTextSchema = z.object({ text: z.string(), tone: z.number().int() });
const int = z.number().int();

export const consoleFrameSchema = z.object({
  screen: z.object({
    page: int,
    recording: z.enum(["noObs", "stopped", "starting", "recording", "paused", "stopping", "switching"]),
    flags: int,
    recordedSec: int,
    pausedSec: int,
    spanKey: int,
    canvas: int,
    activeMask: int,
    offMask: int,
    alert: int,
    labelsVersion: int,
    flags2: int,
    spanSec: int,
  }),
  keys: z.array(
    z.object({
      role: z.enum(["none", "normal", "scene", "marker", "danger", "disabled", "caution"]),
      line1: z.string(),
      line2: z.string(),
      hold: z.boolean(),
      big: z.boolean(),
    }),
  ),
  sceneLine: consoleTextSchema,
  smallLine: consoleTextSchema,
  strip: z.array(z.object({ r: int, g: int, b: int, effect: z.enum(["steady", "blink1Hz", "blink4Hz"]) })),
  // Los textos del aviso grave a la vista; falta si no hay ninguno.
  alert: z.object({ number: int, title: consoleTextSchema, line1: consoleTextSchema, line2: consoleTextSchema, hint: consoleTextSchema }).optional(),
});

export type PreviewSample = z.output<typeof previewSampleSchema>;
export type ConsoleFrame = z.output<typeof consoleFrameSchema>;
