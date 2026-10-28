import { z } from "zod";

// Los ajustes de esta PC (etapa 8): espejo de src/PampaPad.Core/Bridge/SettingsDtos.cs, aparte de contract.ts por el tope de 300
// líneas. startWithWindows sale de Windows; unavailable en una copia sin instalar. La clave de OBS nunca viaja: password dice de dónde sale.
export const settingsViewSchema = z.object({
  hotkeysEnabled: z.boolean(),
  consolePort: z.string().optional(),
  skippedVersion: z.string().optional(),
  startWithWindows: z.enum(["unavailable", "on", "off"]),
  obs: z.object({ address: z.string(), password: z.string() }).optional(),
});

export const settingsRequests = {
  // Sólo lo que cambió; skippedVersion "" borra la versión salteada. Contesta los ajustes como quedaron.
  "settings.save": {
    request: z.strictObject({ startWithWindows: z.boolean().optional(), skippedVersion: z.string().optional() }),
    response: settingsViewSchema,
  },
  // La fase de la actualización llega por state.
  "update.check": { request: z.strictObject({}), response: z.object({}) },
  "update.skip": { request: z.strictObject({}), response: z.object({}) },
} as const;

export type SettingsView = z.output<typeof settingsViewSchema>;
