// Los textos de Estado y de la franja de arriba (05 §4 y §4.4). Puros y sin importar nada (01 §1): reciben los valores tal como viajan
// por el puente. Donde la bandeja ya dice algo, se dice igual (src/PampaPad.Core/State/TraySummary.cs).

export type ObsLink = "closed" | "no_websocket" | "auth_failed" | "loading" | "ready" | "switching" | "lost" | "kicked" | "exiting";
export type Recording = "stopped" | "starting" | "recording" | "paused" | "stopping";
export type ConsoleState = "off" | "waiting" | "connected" | "released";
export type LibraryStatus = "notLoaded" | "loaded" | "noLibrary" | "unreadable" | "newer";
export type UpdatePhase = "idle" | "checking" | "available" | "downloading" | "applying";

export const triggers = ["key", "hotkey", "window", "mcp", "tray", "internal"] as const;
export type TriggerName = (typeof triggers)[number];

/** Como TraySummary.WithoutObs, más "OBS conectado". */
export function obsText(link: ObsLink): string {
  switch (link) {
    case "ready":
      return "OBS conectado";
    case "closed":
      return "Sin OBS: no está abierto";
    case "no_websocket":
      return "Sin OBS: está abierto, sin WebSocket";
    case "auth_failed":
      return "Sin OBS: clave rechazada";
    case "loading":
      return "Sin OBS: conectando";
    case "switching":
      return "Sin OBS: cambiando de colección";
    case "lost":
      return "Sin OBS: se cortó la conexión";
    case "kicked":
      return "Sin OBS: OBS cerró la sesión";
    case "exiting":
      return "Sin OBS: se está cerrando";
  }
}

/** Tiempo grabado como 0:34:12; las horas no se cortan en 24 (TraySummary.Clock). */
export function clock(ms: number): string {
  const total = Math.floor(ms / 1000);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  return `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}

/** Lo que dice de la toma. Sin enlace, "lo último que se supo", como la bandeja. undefined si no hay nada que decir. */
export function recordingText(link: ObsLink, recording: Recording, recordedMs: number, streaming: boolean): string | undefined {
  const known = link === "ready";
  const parts: string[] = [];
  if (recording === "recording") {
    parts.push(known ? `Grabando ${clock(recordedMs)}` : "lo último que se supo: grabando");
  } else if (recording === "paused") {
    parts.push(known ? "En pausa: las marcas no entran" : "lo último que se supo: en pausa");
  } else if (recording === "starting") {
    parts.push("Arrancando la grabación");
  } else if (recording === "stopping") {
    parts.push("Deteniendo la grabación");
  }

  if (streaming && known) {
    parts.push("Transmitiendo");
  }

  return parts.length === 0 ? undefined : parts.join(" · ");
}

/** La consola por cable. "Conectada" quiere decir sólo que el puerto abrió: el saludo de la consola llega en la etapa 9 (05 §4.5). */
export function consoleText(state: ConsoleState, port?: string): string {
  switch (state) {
    case "off":
      return "Sin consola: no hay puerto en local.json";
    case "waiting":
      return `Consola ${port}: sin conexión, reintenta cada 2 s`;
    case "connected":
      return `Consola ${port}: conectada`;
    case "released":
      return `Consola ${port}: suelta, se toma desde la bandeja`;
  }
}

/** Los atajos globales. Apagados es lo normal en la copia de desarrollo (05 §10). */
export function hotkeysText(enabled: boolean, registered: number, rejected: number): string {
  if (!enabled) {
    return "Apagados en esta copia";
  }

  const taken = `${registered} ${registered === 1 ? "registrado" : "registrados"}`;
  return rejected === 0 ? taken : `${taken}, ${rejected} ${rejected === 1 ? "rechazado" : "rechazados"} por Windows`;
}

/** La biblioteca, con los textos de TraySummary.LibraryText donde existen. */
export function libraryText(status: LibraryStatus, folder: string): string {
  switch (status) {
    case "loaded":
      return `Cargada de ${folder}`;
    case "notLoaded":
      return "Todavía no se cargó";
    case "noLibrary":
      return folder === "" ? "Sin biblioteca: elegí la carpeta de datos" : `Sin biblioteca: no encuentro ${folder}`;
    case "unreadable":
      return "config.json no se puede leer";
    case "newer":
      return "config.json es de una versión más nueva que este programa";
  }
}

/** La fase de la actualización, con el motivo de la última falla, que Estado muestra (05 §8). */
export function updateText(phase: UpdatePhase, version?: string, detail?: string): string {
  const text = {
    idle: "Sin versión nueva a la vista",
    checking: "Buscando actualización…",
    available: `Versión ${version} lista para instalar`,
    downloading: `Bajando la versión ${version}`,
    applying: `Instalando la versión ${version}`,
  }[phase];
  return detail === undefined ? text : `${text}. ${detail}`;
}

/** Quién disparó, en castellano (requestedBy, 05 §9). */
export function requestedByText(trigger: TriggerName): string {
  return { key: "una tecla", hotkey: "un atajo", window: "la ventana", mcp: "Claude", tray: "la bandeja", internal: "el programa" }[trigger];
}

export function levelText(level: "info" | "warning" | "error"): string {
  return { info: "info", warning: "aviso", error: "error" }[level];
}

/** "paso n (tipo): mensaje", como el aviso de la bandeja (05 §3). */
export function failureText(stepNumber?: number, stepType?: string, message?: string): string {
  const text = message ?? "falló";
  return stepNumber === undefined ? text : `paso ${stepNumber}${stepType === undefined ? "" : ` (${stepType})`}: ${text}`;
}

/** La hora de una línea del registro, en la hora de la PC: 23:30:04. */
export function timeOfDay(iso: string): string {
  const date = new Date(iso);
  return [date.getHours(), date.getMinutes(), date.getSeconds()].map((part) => String(part).padStart(2, "0")).join(":");
}
