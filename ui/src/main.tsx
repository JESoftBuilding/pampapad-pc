import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { pickBridge } from "./bridge/bridge";
import "./index.css";

const root = document.getElementById("root");
if (root === null) {
  throw new Error("Falta #root en index.html");
}

// Un solo puente para toda la vida de la página: el real dentro de WebView2, el falso en un navegador común (05 §10).
const bridge = pickBridge();

createRoot(root).render(
  <StrictMode>
    <App bridge={bridge} />
  </StrictMode>,
);
