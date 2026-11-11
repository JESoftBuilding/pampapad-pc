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

/** Lo que dijo el último Hello de este enlace; sin Hello, todo falta (plan de la 9a, decisión 13). */
export type ConsoleHello = { firmware?: string; settingsInSync?: boolean };

/** El saludo de la consola: el firmware y si la huella de los ajustes coincide con la que manda la PC. Es el mismo texto que la bandeja. */
export function greetingText(hello: ConsoleHello = {}): string {
  if (hello.firmware === undefined) {
    return "sin saludo todavía";
  }

  return `firmware ${hello.firmware} · ${hello.settingsInSync === true ? "ajustes al día" : "ajustes sin confirmar"}`;
}

/** Lo que el enlace suma al saludo desde la 1.4.0: el receptor por radio y "Automático" (plan de la 1.4.0, decisiones 2 y 6). */
export type ConsoleLinkInfo = ConsoleHello & { receiver?: { hearsConsole: boolean }; auto?: boolean };

/** El puerto como lo nombra la ventana: "Automático · COM5", "Automático" mientras no encuentra placa, o el elegido a mano. Igual que la bandeja. */
export function portLabel(port?: string, link: ConsoleLinkInfo = {}): string {
  if (link.auto !== true) {
    return port ?? "";
  }

  return port === undefined ? "Automático" : `Automático · ${port}`;
}

/**
 * La consola (05 §4.5). Conectada, el saludo dice si la placa contestó: el puerto abierto solo no lo dice. Con el receptor, si la escucha
 * o no (plan de la 1.4.0, decisión 7). Es el mismo texto que la bandeja (TraySummary.Console.cs).
 */
export function consoleText(state: ConsoleState, port?: string, link: ConsoleLinkInfo = {}): string {
  switch (state) {
    case "off":
      return "Sin consola: no hay puerto en local.json";
    case "waiting":
      return link.auto === true && port === undefined
        ? "Automático: no hay un receptor ni una consola enchufados, reintenta cada 2 s"
        : `Consola ${portLabel(port, link)}: sin conexión, reintenta cada 2 s`;
    case "connected":
      if (link.receiver === undefined) {
        return `Consola conectada · ${greetingText(link)}`;
      }

      return link.receiver.hearsConsole ? `Consola conectada por radio · ${greetingText(link)}` : "Receptor conectado · consola sin señal";
    case "released":
      return `Consola ${portLabel(port, link)}: suelta, se toma desde la bandeja`;
  }
}

/** Las luces de la PC por OpenRGB (05 §4.4, 1.6.0): sin zona no se tocan; sin respuesta, PampaPad reintenta solo. */
export function pcLightsText(state: "off" | "connected" | "unreachable", zone?: string): string {
  switch (state) {
    case "off":
      return "Sin manejar: no hay una zona elegida en Ajustes";
    case "connected":
      return `Conectadas a OpenRGB · ${zone ?? ""}`;
    case "unreachable":
      return `OpenRGB no responde · ${zone ?? ""} · reintenta cada 5 s`;
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
