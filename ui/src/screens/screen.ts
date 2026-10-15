import type { Bridge } from "@/bridge/bridge";
import type { SnapshotView } from "@/bridge/useSnapshot";

/** Lo que recibe cada pantalla de la tabla de App.tsx: el puente y la foto con el estado al día. */
export type ScreenProps = { bridge: Bridge; view: SnapshotView };
