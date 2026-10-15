import { useEffect, useRef, useState, type ComponentType } from "react";
import type { Bridge } from "@/bridge/bridge";
import { useSnapshot } from "@/bridge/useSnapshot";
import { Frame, type NavItem } from "@/Frame";
import { Placeholder } from "@/screens/Placeholder";
import type { ScreenProps } from "@/screens/screen";
import { StatusScreen } from "@/screens/status/Status";

export type ScreenRow = NavItem & { Component: ComponentType<ScreenProps> };

/** La tabla de pantallas (01 §6): hash, título y componente, en el orden de 05 §4. stage: la etapa en que llega (01 §8). */
export const screens: ScreenRow[] = [
  {
    hash: "keys",
    title: "Teclas",
    stage: 7,
    Component: () => <Placeholder title="Teclas" stage={7} note="Mientras tanto, las teclas y sus atajos se escriben a mano en config.json." />,
  },
  {
    hash: "actions",
    title: "Acciones",
    stage: 7,
    Component: () => <Placeholder title="Acciones" stage={7} note="Mientras tanto, las acciones se escriben a mano en config.json." />,
  },
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

/** Hasta la etapa 7 la ventana abre en Estado: Teclas, la de "Abrir PampaPad" (05 §3), todavía no existe. */
export const firstScreen = "status";

function hashScreen(): string {
  const hash = window.location.hash.replace(/^#\/?/, "");
  return screens.some((screen) => screen.hash === hash) ? hash : firstScreen;
}

export function App({ bridge }: { bridge: Bridge }) {
  const [current, setCurrent] = useState(hashScreen);
  const view = useSnapshot(bridge);
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

  const screen = screens.find((row) => row.hash === current) ?? screens[3];
  return (
    <Frame screens={screens} current={screen.hash} title={screen.title} view={view}>
      <screen.Component bridge={bridge} view={view} />
    </Frame>
  );
}
