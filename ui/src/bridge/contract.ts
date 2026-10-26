import { z } from "zod";

// El contrato del puente del lado de la página (01 §5): espejo de src/PampaPad.Core/Bridge/Dtos.cs y RequestNames.cs. contract.test.ts
// valida contra estos esquemas cada muestra de contract.samples.json, que escribe tests/PampaPad.Tests/Contract.cs. Lo que C# deja en
// null no viaja (Json.cs omite los nulos): acá es opcional, nunca null.

export const triggerSchema = z.enum(["key", "hotkey", "window", "mcp", "tray", "internal"]);
export const severitySchema = z.enum(["error", "warning"]);
export const logLevelSchema = z.enum(["info", "warning", "error"]);
// Con guion bajo, convertido a mano en ObsView.LinkName: pregunta abierta 59, escrito (b).
export const obsLinkSchema = z.enum(["closed", "no_websocket", "auth_failed", "loading", "ready", "switching", "lost", "kicked", "exiting"]);
export const recordingSchema = z.enum(["stopped", "starting", "recording", "paused", "stopping"]);

export const problemSchema = z.object({ where: z.string(), code: z.string(), message: z.string(), severity: severitySchema });

// Un config_invalid trae en problems todos los de la configuración que no se guardó; message y where son los del primero (E7-2).
export const failureSchema = z.object({ code: z.string(), message: z.string(), where: z.string().optional(), problems: z.array(problemSchema).optional() });

export const stateSchema = z.object({
  seq: z.number().int(),
  update: z.object({
    phase: z.enum(["idle", "checking", "available", "downloading", "applying"]),
    version: z.string().optional(),
    detail: z.string().optional(),
  }),
  obs: z.object({
    link: obsLinkSchema,
    recording: recordingSchema,
    streaming: z.boolean(),
    recordedMs: z.number().int(),
    sceneName: z.string().optional(),
    micName: z.string().optional(),
    micMuted: z.boolean(),
    supportsMarkers: z.boolean(),
    takeInProgress: z.boolean(),
  }),
  library: z.object({ status: z.enum(["notLoaded", "loaded", "noLibrary", "unreadable", "newer"]), folder: z.string() }),
  problems: z.array(problemSchema),
  hotkeys: z.object({ registered: z.number().int(), rejected: z.number().int() }),
  basePage: z.string(),
  activePage: z.string(),
  running: z.array(z.object({ runId: z.string(), actionId: z.string(), requestedBy: triggerSchema })),
  lastFailure: z.object({ noticeId: z.string(), summary: z.string(), message: z.string() }).optional(),
  console: z.object({ state: z.enum(["off", "waiting", "connected", "released"]), port: z.string().optional() }),
});

// config.json se describe en 02 y se tipa en la etapa 7, con la pantalla que lo edita. Hasta entonces viaja entero, sin mirar adentro.
export const configSchema = z.record(z.string(), z.unknown());

export const fieldSchema = z.object({
  name: z.string(),
  label: z.string(),
  kind: z.enum(["text", "option", "obsScene", "obsInput", "json", "number", "duration", "boolean", "script", "window", "page"]),
  optional: z.boolean(),
  options: z.array(z.string()).optional(),
});

export const snapshotSchema = z.object({
  version: z.string(),
  schemaVersion: z.number().int(),
  config: configSchema,
  fingerprint: z.string(),
  state: stateSchema,
  stepTypes: z.array(z.object({ id: z.string(), name: z.string(), group: z.string(), fields: z.array(fieldSchema) })),
  conditionTypes: z.array(z.object({ id: z.string(), name: z.string(), fields: z.array(fieldSchema) })),
  settings: z.object({ hotkeysEnabled: z.boolean(), consolePort: z.string().optional(), skippedVersion: z.string().optional() }),
});

export const logEntrySchema = z.object({
  at: z.string(),
  level: logLevelSchema,
  module: z.string(),
  message: z.string(),
  code: z.string().optional(),
  requestedBy: triggerSchema.optional(),
  runId: z.string().optional(),
});

// "failed" incluye un paso que la cancelación alcanzó antes de correr: notRun queda para uno que nunca se intentó (hallazgo de E3-5).
export const stepReportSchema = z.object({
  outcome: z.enum(["ok", "failed", "omitted", "notRun"]),
  message: z.string().optional(),
  data: z.unknown().optional(),
});

export const actionFinishedSchema = z.object({
  actionId: z.string(),
  runId: z.string(),
  requestedBy: triggerSchema,
  ok: z.boolean(),
  stepNumber: z.number().int().optional(),
  stepType: z.string().optional(),
  failure: failureSchema.optional(),
  durationMs: z.number().int(),
  steps: z.array(stepReportSchema),
});

const noDataSchema = z.strictObject({});

export const requests = {
  "app.snapshot": { request: noDataSchema, response: snapshotSchema },
  "log.read": {
    request: z.strictObject({
      count: z.number().int().min(1).max(500).optional(),
      level: logLevelSchema.optional(),
      requestedBy: triggerSchema.optional(),
      runId: z.string().optional(),
    }),
    response: z.object({ entries: z.array(logEntrySchema) }),
  },
  "action.cancel": {
    request: z.strictObject({ runId: z.string().optional(), all: z.boolean().optional() }),
    response: z.object({ cancelled: z.number().int() }),
  },
  "window.ready": { request: noDataSchema, response: z.object({}) },
  "config.save": {
    request: z.strictObject({ config: configSchema, expectedFingerprint: z.string() }),
    response: z.object({ fingerprint: z.string(), problems: z.array(problemSchema) }),
  },
  "library.reload": { request: noDataSchema, response: z.object({ fingerprint: z.string(), problems: z.array(problemSchema) }) },
} as const;

export const events = {
  state: stateSchema,
  log: logEntrySchema,
  "action.finished": actionFinishedSchema,
  "config.changed": z.object({ config: configSchema, fingerprint: z.string() }),
} as const;

export const errorEnvelopeSchema = z.object({ id: z.number().int(), ok: z.literal(false), error: failureSchema, data: z.unknown().optional() });
export const eventEnvelopeSchema = z.object({ event: z.string(), seq: z.number().int(), data: z.unknown() });

export function requestEnvelope(name: string, data: z.ZodType) {
  return z.strictObject({ id: z.number().int(), request: z.literal(name), data });
}

export function responseEnvelope(data: z.ZodType) {
  return z.object({ id: z.number().int(), ok: z.literal(true), data });
}

export function eventEnvelope(name: string, data: z.ZodType) {
  return z.object({ event: z.literal(name), seq: z.number().int(), data });
}

export type RequestName = keyof typeof requests;
export type EventName = keyof typeof events;
export type RequestData<N extends RequestName> = z.input<(typeof requests)[N]["request"]>;
export type ResponseData<N extends RequestName> = z.output<(typeof requests)[N]["response"]>;
export type EventData<E extends EventName> = z.output<(typeof events)[E]>;
export type Failure = z.output<typeof failureSchema>;
export type State = z.output<typeof stateSchema>;
export type Snapshot = z.output<typeof snapshotSchema>;
export type LogEntry = z.output<typeof logEntrySchema>;
export type ActionFinished = z.output<typeof actionFinishedSchema>;
export type Problem = z.output<typeof problemSchema>;
export type Config = z.output<typeof configSchema>;
export type Trigger = z.output<typeof triggerSchema>;
export type LogLevel = z.output<typeof logLevelSchema>;
