import { z } from "zod";
import { triggerSchema } from "./contract.base";
import { scriptEndSchema, scriptOutputSchema, scriptsRequests } from "./contract.scripts";

// El contrato del puente del lado de la página (01 §5): espejo de src/PampaPad.Core/Bridge/Dtos.cs y RequestNames.cs. contract.test.ts
// valida contra estos esquemas cada muestra de contract.samples.json, que escribe tests/PampaPad.Tests/Contract.cs. Lo que C# deja en
// null no viaja (Json.cs omite los nulos): acá es opcional, nunca null.

export { triggerSchema };
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

// config.json (02 §4), tipado sin cerrar ningún nivel: z.looseObject deja pasar lo que la página no conoce, que vuelve igual en
// config.save (02 §3, regla 1). Los campos conocidos van con el tipo con que los lee el núcleo, que con otro tipo no carga el archivo;
// los tres que el núcleo lee con tolerancia (type, negate y un onlyIf que no es un objeto quedan entre los parámetros: Step.cs y
// Condition.cs) llevan catch, para que un archivo que el núcleo carga no deje a la página sin foto.
export const conditionSchema = z.looseObject({ type: z.string().optional().catch(undefined), negate: z.boolean().optional().catch(undefined) });
export const stepSchema = z.looseObject({ type: z.string().optional().catch(undefined), onlyIf: conditionSchema.optional().catch(undefined) });
export const holdSchema = z.looseObject({ action: z.string().optional() });
export const keySchema = z.looseObject({
  position: z.number().int().optional(),
  label: z.string().optional(),
  shortLabel: z.array(z.string()).optional(),
  action: z.string().optional(),
  hotkey: z.string().optional(),
  onHold: holdSchema.optional(),
});
export const pageSchema = z.looseObject({ id: z.string().optional(), name: z.string().optional(), type: z.string().optional(), keys: z.array(keySchema).optional(), show: z.boolean().optional() });
export const actionSchema = z.looseObject({ id: z.string().optional(), name: z.string().optional(), steps: z.array(stepSchema).optional() });
export const bindingSchema = z.looseObject({ hotkey: z.string().optional(), action: z.string().optional() });
export const preflightCheckSchema = z.looseObject({ condition: conditionSchema.optional(), message: z.string().optional() });
export const configSchema = z.looseObject({
  version: z.number().int().optional(),
  mainPage: z.string().optional(),
  pages: z.array(pageSchema).optional(),
  actions: z.array(actionSchema).optional(),
  hotkeys: z.array(bindingSchema).optional(),
  preflight: z.array(preflightCheckSchema).optional(),
  moduleKey: z.looseObject({ layer: z.string().optional() }).optional(),
  console: z.record(z.string(), z.unknown()).optional(),
});

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
  script: z.string().optional(),
  end: scriptEndSchema.optional(),
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
      script: z.string().optional(),
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
  "hotkeys.suspend": { request: z.strictObject({ suspended: z.boolean() }), response: z.object({}) },
  "action.test": { request: z.strictObject({ action: actionSchema }), response: z.object({ runId: z.string() }) },
  "window.dirty": { request: z.strictObject({ dirty: z.boolean() }), response: z.object({}) },
  "options.list": {
    request: z.strictObject({ list: z.enum(["scenes", "inputs", "scripts", "windows"]) }),
    response: z.object({
      items: z.array(z.object({ name: z.string(), uuid: z.string().optional(), detail: z.string().optional() })),
      unavailable: z.string().optional(),
    }),
  },
  ...scriptsRequests,
} as const;

export const events = {
  state: stateSchema,
  log: logEntrySchema,
  "action.finished": actionFinishedSchema,
  "config.changed": z.object({ config: configSchema, fingerprint: z.string() }),
  "script.output": scriptOutputSchema,
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
export type Page = z.output<typeof pageSchema>;
export type Key = z.output<typeof keySchema>;
export type Action = z.output<typeof actionSchema>;
export type Step = z.output<typeof stepSchema>;
export type Condition = z.output<typeof conditionSchema>;
export type Field = z.output<typeof fieldSchema>;
export type StepType = Snapshot["stepTypes"][number];
export type ConditionType = Snapshot["conditionTypes"][number];
export type Trigger = z.output<typeof triggerSchema>;
export type LogLevel = z.output<typeof logLevelSchema>;
