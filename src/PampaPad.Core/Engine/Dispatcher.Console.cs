namespace PampaPad.Core;

// La perilla y la tecla del módulo (plan de la 9a, decisiones 8 y 9; 02 §6 y §7). Aparte por el tope de 300 líneas (01 §1).
public sealed partial class Dispatcher
{
    // Mientras está apretada rige moduleKey.layer. Se escribe sólo bajo _refreshing.
    private bool _moduleHeld;

    // Si mientras estaba apretada se tocó otra tecla: entonces soltarla no es un toque y no vuelve a la principal.
    private bool _moduleCombined;

    // Si el apretar anterior de la tecla del módulo fue combinación: el primero de un doble toque no puede serlo (plan de la 1.3.0, decisión 4).
    private bool _previousCombined;

    // La página en que se apretó cada tecla de la consola: un toque o un mantenido que sale al soltar, con la del módulo ya suelta, es
    // de la capa en que se apretó (si no, la capa quedaría pegada en ese gesto o la acción sería de otra página).
    private readonly string?[] _pressedOn = new string?[Validator.KeyCount + 1];

    /// <summary>Cuánto cambia el brillo cada detente de la perilla en brightness: lo mismo que hace el firmware sin PC (decisión 45).</summary>
    public const int BrightnessPerDetent = 16;

    /// <summary>El toque del pulsador de la perilla, también grabando: atiende el aviso grave a la vista (plan de la 9b, decisión 8).</summary>
    public event System.Action? KnobTapped;

    // Devuelve true si el evento era de la perilla o de la tecla del módulo, y ya quedó atendido.
    private bool HandleConsoleControl(InputEvent e)
    {
        if (e.Control == Controls.Knob)
        {
            // Con knob en brightness en la página que se ve, el giro es brillo, también grabando (plan de la 1.3.0, decisión 7). Si no,
            // grabando, en pausa, arrancando o deteniendo el giro se ignora sin aviso ni registro (02 §7, regla 1). El clic atiende avisos
            // graves con un toque; Press y Release sueltos no hacen nada.
            if (e.Gesture == Gesture.Turn && e.Value != 0 && KnobIsBrightness())
            {
                _ = AdjustBrightnessAsync(e.Value);
            }
            else if (e.Gesture == Gesture.Turn && _state.Current.Obs.Recording == RecordingState.Stopped)
            {
                for (int step = 0; step < Math.Abs(e.Value); step++)
                {
                    _ = Go(e.Value > 0 ? Navigation.Next : Navigation.Previous);
                }
            }
            else if (e.Gesture == Gesture.Tap)
            {
                KnobTapped?.Invoke();
            }

            return true;
        }

        if (e.Control != Controls.ModuleKey)
        {
            return false;
        }

        switch (e.Gesture)
        {
            case Gesture.Press:
                SetModuleHeld(true);
                break;
            case Gesture.Release:
                SetModuleHeld(false);
                break;
            case Gesture.Tap when !Volatile.Read(ref _moduleCombined):
                // Va en el código: ninguna configuración lo pisa (02 §6). Grabando ya rige la principal y Go falla sin cambiar nada.
                _ = Go(Navigation.Main);
                break;
            case Gesture.DoubleTap when !Volatile.Read(ref _moduleCombined) && !Volatile.Read(ref _previousCombined):
                // Como la acción de una tecla de la consola, también grabando: lo que no vale grabando lo frenan sus pasos.
                if (_library.Current.ModuleKey?.DoubleTap is { } action)
                {
                    _ = Launch(action, new Invocation(Guid.NewGuid(), Trigger.Key, e.Source, e.Control, e.Gesture, e.Value));
                }

                break;
        }

        return true;
    }

    private void SetModuleHeld(bool held)
    {
        lock (_refreshing)
        {
            if (held)
            {
                _previousCombined = _moduleCombined;
                _moduleCombined = false;
            }

            _moduleHeld = held;
            PublishActive(_library.Current);
        }
    }

    // Si la página que se ve (la capa, la de encima o la base) tiene la perilla en brillo.
    private bool KnobIsBrightness()
    {
        lock (_refreshing)
        {
            Config config = _library.Current;
            return config.FindPage(KeysPage(config, _state.Current, HeldLayer(config), _overlay))?.Knob == Page.KnobBrightness;
        }
    }

    // El adjustBrightness del paso lights, por el mismo camino, sin correr una acción: un giro no deja corridas en el registro. Una falla
    // queda como advertencia y no suena (decisión 7). Las luces de la PC lo reciben antes del primer await, también sin consola (1.6.0).
    private async Task AdjustBrightnessAsync(int detents)
    {
        try
        {
            LightCommand command = Lights.AdjustBrightness(Math.Clamp(BrightnessPerDetent * detents, -Lights.MaxAdjust, Lights.MaxAdjust));
            Lights.CopyToPc(_copyToPc, command);
            string line = ConsoleProtocol.LightLine(command);
            Result sent = _sendToConsole is { } send
                ? await send(line, CancellationToken.None).ConfigureAwait(false)
                : Result.Fail(ErrorCodes.ConsoleDisconnected, "No hay consola en este programa.");
            if (sent.Failure is { } failure)
            {
                _log.Warning("dispatcher", $"La perilla no pudo cambiar el brillo: {failure.Message}", failure.Code);
            }
        }
        catch (Exception error)
        {
            _log.Warning("dispatcher", $"La perilla no pudo cambiar el brillo: {error.Message}", ErrorCodes.InternalError);
        }
    }

    // La capa que rige ahora: la de moduleKey.layer mientras se mantiene la tecla del módulo, si es una página de teclas. Bajo _refreshing.
    private string? HeldLayer(Config config) =>
        _moduleHeld && config.ModuleKey?.Layer is { } layer && config.FindPage(layer) is { } page && IsKeys(page) ? layer : null;

    // Una tecla de la consola: si se aprieta con la del módulo, es una combinación. Se recuerda en qué página se apretó.
    private string KeysPageOfConsoleKey(Config config, AppState now, InputEvent e, int position)
    {
        lock (_refreshing)
        {
            if (e.Gesture == Gesture.Press)
            {
                _moduleCombined |= _moduleHeld;
                _pressedOn[position] = KeysPage(config, now, HeldLayer(config), _overlay);
            }

            return _pressedOn[position] ?? KeysPage(config, now, null, _overlay);
        }
    }
}
