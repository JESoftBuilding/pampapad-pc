using PampaPad.Core;

namespace PampaPad.App;

// El vigía de los avisos graves (plan de la 9b, E9b-3), aparte por el tope de 300 líneas (01 §1). Arranca y se suelta con la consola.
internal sealed partial class Composition
{
    private AlertWatch? _alerts;

    /// <summary>El clic de la perilla lo atiende; el sonido de la PC es del Notifier; el chequeo al empezar a grabar, del Preflight.</summary>
    private void ComposeAlerts(StateStore state, Library library, Preflight preflight, Dispatcher dispatcher, Notifier notifier, Log log, TimeProvider clock)
    {
        _alerts = new AlertWatch(state, () => library.Current, preflight.CheckTakeAsync, log, clock, notifier.OnAlertAppeared);
        dispatcher.KnobTapped += _alerts.Attend;
    }
}
