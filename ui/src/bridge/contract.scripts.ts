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
    request: z.strictObject({ id: z.string() }),
    response: z.object({
      script: scriptViewSchema,
      manifest: scriptManifestSchema.optional(),
      files: z.array(z.object({ name: z.string(), size: z.number().int(), modified: z.string() })),
      fingerprint: z.string(),
      runs: z.array(scriptRunSchema),
    }),
  },
} as const;

export type ScriptEnd = z.output<typeof scriptEndSchema>;
export type ScriptRun = z.output<typeof scriptRunSchema>;
export type ScriptView = z.output<typeof scriptViewSchema>;
export type ScriptManifest = z.output<typeof scriptManifestSchema>;
