namespace PampaPad.Core;

// La perilla y la tecla del módulo (plan de la 9a, decisiones 8 y 9; 02 §6 y §7). Aparte por el tope de 300 líneas (01 §1).
public sealed partial class Dispatcher
{
    // Mientras está apretada rige moduleKey.layer. Se escribe sólo bajo _refreshing.
    private bool _moduleHeld;

    // Si mientras estaba apretada se tocó otra tecla: entonces soltarla no es un toque y no vuelve a la principal.
    private bool _moduleCombined;

    // La página en que se apretó cada tecla de la consola: un toque o un mantenido que sale al soltar, con la del módulo ya suelta, es
    // de la capa en que se apretó (si no, la capa quedaría pegada en ese gesto o la acción sería de otra página).
    private readonly string?[] _pressedOn = new string?[Validator.KeyCount + 1];

    /// <summary>El toque del pulsador de la perilla, también grabando: atiende el aviso grave a la vista (plan de la 9b, decisión 8).</summary>
    public event System.Action? KnobTapped;

    // Devuelve true si el evento era de la perilla o de la tecla del módulo, y ya quedó atendido.
    private bool HandleConsoleControl(InputEvent e)
    {
        if (e.Control == Controls.Knob)
        {
            // Grabando, en pausa, arrancando o deteniendo el giro se ignora sin aviso ni registro (02 §7, regla 1). El clic atiende avisos
            // graves con un toque; Press y Release sueltos no hacen nada.
            if (e.Gesture == Gesture.Turn && _state.Current.Obs.Recording == RecordingState.Stopped)
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
        }

        return true;
    }

    private void SetModuleHeld(bool held)
    {
        lock (_refreshing)
        {
            if (held)
            {
                _moduleCombined = false;
            }

            _moduleHeld = held;

            // Sólo cambia la página que se ve: las fuentes siguen con la base, y una capa no vuelve a registrar atajos (02 §7, regla 6).
            Config config = _library.Current;
            string main = config.ResolveMainPage() ?? "";
            AppState now = _state.Current;
            string active = Navigation.Resolve(new NavigationInput(now.BasePage, HeldLayer(config), now.Obs.Recording, main, null, [])).ActivePage;
            _state.Update(s => s with { ActivePage = active });
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
                _pressedOn[position] = KeysPage(config, now, HeldLayer(config));
            }

            return _pressedOn[position] ?? KeysPage(config, now, null);
        }
    }
}
