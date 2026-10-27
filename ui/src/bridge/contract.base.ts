import { z } from "zod";

// Lo que comparten contract.ts y contract.scripts.ts. Aparte para que ninguno de los dos importe al otro en los dos sentidos: con un
// ciclo, un esquema queda sin definir al cargar.
export const triggerSchema = z.enum(["key", "hotkey", "window", "mcp", "tray", "internal"]);
