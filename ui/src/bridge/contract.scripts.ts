import { z } from "zod";
import { triggerSchema } from "./contract.base";

// Los esquemas de los scripts (etapa 8): espejo de src/PampaPad.Core/Bridge/ScriptsDtos.cs, aparte de contract.ts por el tope de 300
// líneas. contract.ts los suma a sus pedidos.

// La línea de cierre de una corrida (LogEntry.end en C#): exitCode falta si el script no salió solo; message es su última línea.
export const scriptEndSchema = z.object({
  ok: z.boolean(),
  exitCode: z.number().int().optional(),
  durationMs: z.number().int(),
  message: z.string().optional(),
});

export const scriptRunSchema = z.object({ at: z.string(), requestedBy: triggerSchema.optional(), end: scriptEndSchema });

export const scriptPlaceSchema = z.object({
  kind: z.enum(["key", "hold", "hotkey"]),
  where: z.string(),
  page: z.string().optional(),
  pageName: z.string().optional(),
  position: z.number().int().optional(),
  label: z.string().optional(),
  hotkey: z.string().optional(),
});

export const scriptViewSchema = z.object({
  id: z.string(),
  name: z.string(),
  description: z.string().optional(),
  entry: z.string().optional(),
  wait: z.boolean(),
  timeoutSec: z.number().int(),
  contract: z.number().int().optional(),
  tags: z.array(z.string()),
  status: z.enum(["ready", "running", "problem", "noManifest"]),
  problems: z.array(z.object({ code: z.string(), message: z.string() })),
  usedBy: z.array(z.object({ action: z.string(), actionName: z.string().optional(), where: z.string(), places: z.array(scriptPlaceSchema) })),
  lastRun: scriptRunSchema.optional(),
});

// La ficha cruda, script.json: lo que el programa no conoce viaja también y vuelve igual.
export const scriptManifestSchema = z.looseObject({
  entry: z.string().optional(),
  name: z.string().optional(),
  description: z.string().optional(),
  wait: z.boolean().optional(),
  timeoutSec: z.number().int().optional(),
  contract: z.number().int().optional(),
  tags: z.array(z.string()).optional(),
});

export const scriptsRequests = {
  "scripts.list": { request: z.strictObject({}), response: z.object({ scripts: z.array(scriptViewSchema) }) },
  "scripts.read": {
    // content y text son del MCP: la ventana no edita código y no los pide.
    request: z.strictObject({ id: z.string(), content: z.boolean().optional() }),
    response: z.object({
      script: scriptViewSchema,
      manifest: scriptManifestSchema.optional(),
      files: z.array(z.object({ name: z.string(), size: z.number().int(), modified: z.string(), text: z.string().optional() })),
      fingerprint: z.string(),
      runs: z.array(scriptRunSchema),
    }),
  },
  "scripts.write": {
    // files es del MCP (crear o reemplazar los archivos de texto); la ventana no lo manda.
    request: z.strictObject({
      id: z.string(),
      manifest: scriptManifestSchema,
      expectedFingerprint: z.string(),
      files: z.array(z.object({ name: z.string(), text: z.string() })).optional(),
    }),
    response: z.object({ fingerprint: z.string() }),
  },
  // Cancelar el diálogo de Windows contesta bien, con el id vacío.
  "scripts.import": { request: z.strictObject({}), response: z.object({ id: z.string() }) },
  "scripts.delete": { request: z.strictObject({ id: z.string() }), response: z.object({ backupPath: z.string() }) },
  "scripts.reveal": { request: z.strictObject({ id: z.string().optional() }), response: z.object({}) },
  "library.reveal": { request: z.strictObject({}), response: z.object({}) },
  // Contesta el runId en el acto: las líneas y el final llegan por script.output.
  "scripts.test": { request: z.strictObject({ id: z.string() }), response: z.object({ runId: z.string() }) },
} as const;

// El evento script.output (ScriptOutputEvent en C#): un renglón con su salida, o el final con la línea de cierre. notice es el aviso
// del corte en 2000 líneas, que no es del script.
export const scriptOutputSchema = z.object({
  runId: z.string(),
  line: z.string().optional(),
  stream: z.enum(["stdout", "stderr", "notice"]).optional(),
  end: scriptEndSchema.optional(),
});

export type ScriptEnd = z.output<typeof scriptEndSchema>;
export type ScriptRun = z.output<typeof scriptRunSchema>;
export type ScriptView = z.output<typeof scriptViewSchema>;
export type ScriptManifest = z.output<typeof scriptManifestSchema>;
export type ScriptOutput = z.output<typeof scriptOutputSchema>;
