import type { Bridge } from "@/bridge/bridge";
import type { SnapshotView } from "@/bridge/useSnapshot";
import type { Editor } from "./common/useDraft";

/** Lo que recibe cada pantalla de la tabla de App.tsx: el puente, la foto con el estado al día y el borrador, que es uno solo por ventana. */
export type ScreenProps = { bridge: Bridge; view: SnapshotView; editor: Editor };
