import { useEffect, useRef, useState, type ComponentType } from "react";
import type { Bridge } from "@/bridge/bridge";
import { useSnapshot } from "@/bridge/useSnapshot";
import { Frame, type NavItem } from "@/Frame";
import { useDraft } from "@/screens/common/useDraft";
import { ActionsScreen } from "@/screens/actions/Actions";
import { KeysScreen } from "@/screens/keys/Keys";
import { Placeholder } from "@/screens/Placeholder";
import type { ScreenProps } from "@/screens/screen";
import { StatusScreen } from "@/screens/status/Status";

export type ScreenRow = NavItem & { Component: ComponentType<ScreenProps> };

/** La tabla de pantallas (01 §6): hash, título y componente, en el orden de 05 §4. stage: la etapa en que llega (01 §8). */
export const screens: ScreenRow[] = [
  { hash: "keys", title: "Teclas", Component: KeysScreen },
  { hash: "actions", title: "Acciones", Component: ActionsScreen },
  {
    hash: "scripts",
    title: "Scripts",
    stage: 8,
    Component: () => <Placeholder title="Scripts" stage={8} note="Los scripts entran en las acciones con la etapa 5; su pantalla, en la 8." />,
  },
  { hash: "status", title: "Estado", Component: StatusScreen },
  {
    hash: "settings",
    title: "Ajustes",
    stage: 8,
    Component: () => <Placeholder title="Ajustes" stage={8} note="Mientras tanto, los ajustes de esta PC van a mano en local.json." />,
  },
];

/** La de "Abrir PampaPad" y la de un hash que no es de ninguna pantalla: Teclas (05 §3), desde la etapa 7. */
export const firstScreen = "keys";

/** La pantalla del hash, sin lo que sigue al signo de pregunta (#/keys?page=grabacion&position=5 es Teclas; la selección la lee la pantalla). */
function hashScreen(): string {
  const hash = window.location.hash.replace(/^#\/?/, "").split("?")[0];
  return screens.some((screen) => screen.hash === hash) ? hash : firstScreen;
}

export function App({ bridge }: { bridge: Bridge }) {
  const [current, setCurrent] = useState(hashScreen);
  const view = useSnapshot(bridge);
  // Un solo borrador por ventana (05 §11 define un solo window.dirty): pasar de una pantalla a otra no descarta lo editado.
  const editor = useDraft(bridge, view);
  const announced = useRef(false);

  useEffect(() => {
    const onHash = () => setCurrent(hashScreen());
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  // window.ready, una sola vez y con la foto ya pedida: la ventana se muestra con la pantalla llena (01 §5).
  useEffect(() => {
    if (announced.current || (view.snapshot === undefined && view.failure === undefined)) {
      return;
    }

    announced.current = true;
    void bridge.request("window.ready", {});
  }, [bridge, view.snapshot, view.failure]);

  // window.dirty (05 §11): la cáscara pregunta al cerrar sólo si hay cambios sin guardar. Vive acá y no en una pantalla: si no, al
  // navegar se mandaría dirty false con el borrador todavía sin guardar.
  useEffect(() => {
    void bridge.request("window.dirty", { dirty: editor.dirty });
  }, [bridge, editor.dirty]);

  const screen = screens.find((row) => row.hash === current) ?? screens[0];
  return (
    <Frame screens={screens} current={screen.hash} title={screen.title} view={view}>
      <screen.Component bridge={bridge} view={view} editor={editor} />
    </Frame>
  );
}
