import { z } from "zod";

// La zona de OpenRGB de las luces de la PC, por nombre (plan de la 1.6.0, decisión 7).
export const pcLightsChoiceSchema = z.object({ device: z.string(), zone: z.string() });

// Los ajustes de esta PC (etapa 8): espejo de src/PampaPad.Core/Bridge/SettingsDtos.cs, aparte de contract.ts por el tope de 300
// líneas. startWithWindows sale de Windows; unavailable en una copia sin instalar. La clave de OBS nunca viaja: password dice de dónde sale.
export const settingsViewSchema = z.object({
  hotkeysEnabled: z.boolean(),
  consolePort: z.string().optional(),
  skippedVersion: z.string().optional(),
  startWithWindows: z.enum(["unavailable", "on", "off"]),
  obs: z.object({ address: z.string(), password: z.string() }).optional(),
  // El interruptor del MCP y el comando para registrarlo en Claude Code; mcpCommand falta en una copia sin instalar (etapa MCP).
  mcpEnabled: z.boolean(),
  mcpCommand: z.string().optional(),
  // La zona elegida para las luces de la PC; falta con "No manejar" (1.6.0).
  pcLights: pcLightsChoiceSchema.optional(),
});

export const consolePortSchema = z.object({ port: z.string(), description: z.string(), usbId: z.string().optional() });

export const settingsRequests = {
  // Sólo lo que cambió; skippedVersion "" borra la versión salteada. Contesta los ajustes como quedaron.
  "settings.save": {
    request: z.strictObject({
      startWithWindows: z.boolean().optional(),
      skippedVersion: z.string().optional(),
      mcpEnabled: z.boolean().optional(),
      // "" es "Sin consola" y borra la clave; el puerto se usa al volver a abrir PampaPad (etapa 8b).
      consolePort: z.string().optional(),
      // Una zona, o device y zone vacíos para "No manejar", que saca pcLights de local.json. Vale en el acto (1.6.0).
      pcLights: z.strictObject({ device: z.string(), zone: z.string() }).optional(),
    }),
    response: settingsViewSchema,
  },
  // Los puertos serie que están, con la descripción de Windows y el VID:PID si es USB (etapa 8b). Sólo la ventana.
  "console.ports": { request: z.strictObject({}), response: z.object({ ports: z.array(consolePortSchema) }) },
  // La fase de la actualización llega por state.
  "update.check": { request: z.strictObject({}), response: z.object({}) },
  "update.skip": { request: z.strictObject({}), response: z.object({}) },
} as const;

export type SettingsView = z.output<typeof settingsViewSchema>;
export type ConsolePort = z.output<typeof consolePortSchema>;
export type PcLightsChoice = z.output<typeof pcLightsChoiceSchema>;
