import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from "react";
import type { Bridge } from "@/bridge/bridge";
import { fromKeyPress, parseHotkey } from "@/model/hotkeys";
import { Button } from "@/ui/button";
import { Field } from "@/ui/field";
import { Input } from "@/ui/input";

/** El tope de la captura en la página, antes de los 30 s en que el programa reanuda solo (plan de la etapa 8, E8-10). */
export const captureLimitMs = 25_000;
export const captureTimeoutText = "Se acabó el tiempo, volvé a grabar.";

/**
 * El atajo de una tecla con su botón de grabar (05 §6): al grabar, la página pide hotkeys.suspend y el programa suelta sus atajos
 * globales (una combinación registrada no llega a la página, E0-3); escucha el teclado, arma el texto con la gramática de 02 §4.3 y,
 * al terminar, cancelar con Esc o perder el foco, pide reanudar. También se escribe a mano, para la copia que no toma los atajos.
 * taken dice dónde ya se usa una combinación ("la tecla 6 de Grabación", "un atajo general"): esa no se acepta y la captura sigue.
 * La usan Teclas y los atajos generales de Ajustes: id, label y hint son de cada uno. Siempre reanuda: si se desmonta mientras espera
 * la respuesta de suspender, al volver reanuda y sale; y a los 25 s cierra sola, antes de que el programa reanude solo a los 30.
 * clearable apaga "Quitar atajo" donde la combinación es obligatoria (los atajos generales: la fila se saca con su propio Quitar).
 */
export function HotkeyCapture({
  id,
  label,
  hint,
  value,
  onChange,
  bridge,
  disabledReason,
  taken,
  clearable = true,
}: {
  id: string;
  label: string;
  hint: string;
  value?: string;
  onChange: (hotkey: string | undefined) => void;
  bridge: Bridge;
  disabledReason?: string;
  taken: (hotkey: string) => string | null;
  clearable?: boolean;
}) {
  const [capturing, setCapturing] = useState(false);
  const [notice, setNotice] = useState<string>();
  const [text, setText] = useState(value ?? "");
  const active = useRef(false);
  const mounted = useRef(true);
  const box = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setText(value ?? "");
  }, [value]);

  useEffect(() => {
    if (capturing) {
      box.current?.focus();
    }
  }, [capturing]);

  // Reanudar una sola vez por captura, pase lo que pase: una tecla aceptada hace perder el foco y los dos caminos llegan acá.
  const stop = useCallback(() => {
    if (!active.current) {
      return;
    }

    active.current = false;
    setCapturing(false);
    void bridge.request("hotkeys.suspend", { suspended: false });
  }, [bridge]);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      stop();
    };
  }, [stop]);

  // El reloj de la captura: el programa reanuda solo a los 30 s (hotkeys.suspend); la página cierra antes y lo dice.
  useEffect(() => {
    if (!capturing) {
      return;
    }

    const timer = window.setTimeout(() => {
      setNotice(captureTimeoutText);
      stop();
    }, captureLimitMs);
    return () => window.clearTimeout(timer);
  }, [capturing, stop]);

  const start = async () => {
    setNotice(undefined);
    const reply = await bridge.request("hotkeys.suspend", { suspended: true });
    if (!reply.ok) {
      setNotice(reply.error.message);
      return;
    }

    if (!mounted.current) {
      // Se fue de la pantalla mientras esperaba: nadie va a capturar, se reanuda ya.
      void bridge.request("hotkeys.suspend", { suspended: false });
      return;
    }

    active.current = true;
    setCapturing(true);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    event.preventDefault();
    if (event.key === "Escape") {
      stop();
      return;
    }

    const capture = fromKeyPress(event);
    if (capture.kind === "hotkey") {
      const owner = taken(capture.text);
      if (owner !== null) {
        setNotice(`Repetida en ${owner}: ${capture.text}. Probá otra combinación.`);
        return;
      }

      setNotice(undefined);
      onChange(capture.text);
      stop();
    } else if (capture.kind === "rejected") {
      setNotice(capture.reason);
    }
  };

  const onTyped = () => {
    if (text.trim() === "") {
      setNotice(undefined);
      if (value !== undefined) {
        onChange(undefined);
      }

      return;
    }

    const parsed = parseHotkey(text);
    if (parsed === null) {
      setNotice(`No se entiende ${text}: uno a tres de Ctrl, Shift y Alt y una tecla, como Ctrl+Shift+Alt+F8.`);
      return;
    }

    const owner = parsed === value ? null : taken(parsed);
    if (owner !== null) {
      setNotice(`Repetida en ${owner}: ${parsed}.`);
      return;
    }

    setNotice(undefined);
    if (parsed !== value) {
      onChange(parsed);
    }
  };

  return (
    <Field label={label} htmlFor={id} hint={disabledReason ?? hint} error={notice}>
      <div className="flex flex-wrap items-center gap-2">
        <Input id={id} className="w-48" value={text} onChange={(event) => setText(event.target.value)} onBlur={onTyped} placeholder="Sin atajo" />
        <Button size="sm" variant="outline" onClick={() => void start()} disabled={capturing || disabledReason !== undefined}>
          Grabar atajo
        </Button>
        {clearable && (
          <Button size="sm" variant="ghost" onClick={() => onChange(undefined)} disabled={value === undefined}>
            Quitar atajo
          </Button>
        )}
      </div>
      {capturing && (
        <div ref={box} tabIndex={0} onKeyDown={onKeyDown} onBlur={stop} className="rounded-md border border-accent bg-background px-3 py-2 text-sm text-accent outline-none">
          Apretá la combinación: los atajos globales están suspendidos hasta que termines.
        </div>
      )}
    </Field>
  );
}
