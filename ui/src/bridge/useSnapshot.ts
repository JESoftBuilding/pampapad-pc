import { useEffect, useReducer } from "react";
import type { Bridge } from "./bridge";
import type { Config, Failure, Snapshot, State } from "./contract";

/** Lo que la página sabe del programa: la foto de app.snapshot y el estado al día. */
export type SnapshotView = { snapshot?: Snapshot; state?: State; failure?: Failure };

export type SnapshotMessage =
  | { kind: "snapshot"; snapshot: Snapshot }
  | { kind: "state"; state: State }
  | { kind: "config"; config: Config; fingerprint: string }
  | { kind: "failed"; failure: Failure };

/** La regla de 01 §4.4: un estado entra sólo si su seq es mayor que el que ya se tiene. */
export function newer(current: State | undefined, incoming: State): State {
  return current === undefined || incoming.seq > current.seq ? incoming : current;
}

export function reduce(view: SnapshotView, message: SnapshotMessage): SnapshotView {
  switch (message.kind) {
    case "snapshot":
      return { snapshot: message.snapshot, state: newer(view.state, message.snapshot.state) };
    case "state":
      return { ...view, state: newer(view.state, message.state) };
    case "config":
      return view.snapshot === undefined ? view : { ...view, snapshot: { ...view.snapshot, config: message.config, fingerprint: message.fingerprint } };
    case "failed":
      return { ...view, failure: message.failure };
  }
}

/**
 * La foto al abrir más la suscripción (01 §5). Escucha antes de pedir: lo que llegue mientras tanto no se pierde, y newer descarta lo
 * que sea más viejo que la foto.
 */
export function useSnapshot(bridge: Bridge): SnapshotView {
  const [view, dispatch] = useReducer(reduce, {});

  useEffect(() => {
    let alive = true;
    const offState = bridge.on("state", (state) => dispatch({ kind: "state", state }));
    const offConfig = bridge.on("config.changed", (changed) => dispatch({ kind: "config", config: changed.config, fingerprint: changed.fingerprint }));
    void bridge.request("app.snapshot", {}).then((reply) => {
      if (alive) {
        dispatch(reply.ok ? { kind: "snapshot", snapshot: reply.data } : { kind: "failed", failure: reply.error });
      }
    });

    return () => {
      alive = false;
      offState();
      offConfig();
    };
  }, [bridge]);

  return view;
}
