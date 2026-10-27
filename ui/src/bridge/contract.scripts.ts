import { z } from "zod";

// Los esquemas de los scripts (etapa 8), aparte de contract.ts por el tope de 300 líneas. Sólo importa zod: contract.ts los importa a
// ellos, y al revés quedaría un ciclo que deja un esquema sin definir al cargar.

// La línea de cierre de una corrida (LogEntry.end en C#): exitCode falta si el script no salió solo; message es su última línea.
export const scriptEndSchema = z.object({
  ok: z.boolean(),
  exitCode: z.number().int().optional(),
  durationMs: z.number().int(),
  message: z.string().optional(),
});

export type ScriptEnd = z.output<typeof scriptEndSchema>;
