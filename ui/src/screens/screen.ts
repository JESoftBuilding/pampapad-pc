import type { Bridge } from "@/bridge/bridge";
import type { SnapshotView } from "@/bridge/useSnapshot";
import type { Editor } from "./common/useDraft";
import type { ScriptEditor } from "./scripts/useScriptDraft";

/** Lo que recibe cada pantalla de la tabla de App.tsx: el puente, la foto con el estado al día y los borradores, que son uno de cada uno por ventana. */
export type ScreenProps = { bridge: Bridge; view: SnapshotView; editor: Editor; script: ScriptEditor };
