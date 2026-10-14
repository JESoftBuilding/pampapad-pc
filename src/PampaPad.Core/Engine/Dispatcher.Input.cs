namespace PampaPad.Core;

// La entrada del Dispatcher: del evento de una fuente a la acción. Está aparte para no pasar el tope de 300 líneas (01 §1).
public sealed partial class Dispatcher
{
    /// <summary>Cada cuánto corre el reloj de los gestos si no llega nada (Propuesto, sin medir).</summary>
    public static readonly TimeSpan GestureTick = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// La puerta de todas las fuentes. Llega en el hilo de la fuente, así que sólo busca la acción y la lanza. Nunca tira.
    /// Lo crudo (Press, Release, Turn) viene de una consola y pasa por Gestures, porque los gestos se reconocen en la PC (04 §8).
    /// Lo ya reconocido, el toque de un atajo de teclado, sigue derecho (01 §3.1).
    /// </summary>
    public void Receive(InputEvent e)
    {
        try
        {
            if (e.Gesture is Gesture.Press or Gesture.Release or Gesture.Turn)
            {
                foreach (InputEvent recognized in _gestures.Feed(e))
                {
                    Handle(recognized, viaGestures: true);
                }

                return;
            }

            Handle(e, viaGestures: false);
        }
        catch (Exception error)
        {
            _log.Error("dispatcher", $"No se pudo atender {e.Control}: {error.Message}", ErrorCodes.InternalError);
        }
    }

    /// <summary>Hace correr el reloj de los gestos: saca el mantenido que llegó al umbral y da por soltado lo que perdió frescura.</summary>
    public void Tick()
    {
        try
        {
            foreach (InputEvent recognized in _gestures.Tick())
            {
                Handle(recognized, viaGestures: true);
            }
        }
        catch (Exception error)
        {
            _log.Error("dispatcher", $"No se pudo hacer correr el reloj de los gestos: {error.Message}", ErrorCodes.InternalError);
        }
    }

    /// <summary>El bucle que llama a Tick. Sin él, un mantenido sólo saldría con el próximo estado que mande la placa.</summary>
    public async Task KeepTickingAsync(TimeProvider clock, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(GestureTick, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
            _log.Info("dispatcher", "Se detuvo el reloj de los gestos.");
        }
    }

    /// <summary>
    /// La tabla de gestos de 02 §6. Una tecla sin onHold dispara su acción al apretar. Con onHold, la
    /// acción sale con el toque y la de onHold con el mantenido. Un atajo de teclado dispara siempre la
    /// acción del toque, nunca la de onHold. Devuelve null si ese gesto no dispara nada.
    /// </summary>
    public static string? ActionOf(Key key, Gesture gesture, bool viaGestures)
    {
        if (!viaGestures)
        {
            return gesture == Gesture.Tap ? key.Action : null;
        }

        if (key.OnHold is null)
        {
            return gesture == Gesture.Press ? key.Action : null;
        }

        return gesture switch
        {
            Gesture.Tap => key.Action,
            Gesture.Hold => key.OnHold.Action,
            _ => null,
        };
    }

    private void Handle(InputEvent e, bool viaGestures)
    {
        Config config = _library.Current;
        string? actionId = null;
        if (e.Control == Controls.Hotkey)
        {
            // Un atajo de la lista general: sólo existe como toque del teclado.
            if (!viaGestures && e.Gesture == Gesture.Tap && config.Hotkeys is { } general && e.Value >= 0 && e.Value < general.Count)
            {
                actionId = general[e.Value]?.Action;
            }
        }
        else if (Controls.TryKey(e.Control, out int position))
        {
            // Base vacía es "no hay página" (Refresh no la toma ni para una página con el id vacío): no hay tecla a la que llevar el gesto.
            string basePage = _state.Current.BasePage;
            Key? key = basePage.Length == 0
                ? null
                : config.FindPage(basePage)?.Keys.Indexed().Select(item => item.Item).FirstOrDefault(candidate => candidate.Position == position);
            actionId = key is null ? null : ActionOf(key, e.Gesture, viaGestures);
        }

        if (actionId is null)
        {
            return;
        }

        Trigger trigger = e.Source == HotkeySource.SourceId ? Trigger.Hotkey : Trigger.Key;
        _ = Launch(actionId, new Invocation(Guid.NewGuid(), trigger, e.Source, e.Control, e.Gesture, e.Value));
    }
}
