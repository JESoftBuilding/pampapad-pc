namespace PampaPad.Core;

/// <summary>
/// Lo que necesitan los pasos de cadenas, además de OBS: la biblioteca (scripts y páginas), las ventanas, quien corre los scripts y quien
/// cambia de página. GoToPage es Dispatcher.Go, que se arma después del catálogo (01 §4.3): por eso llega como función. SendToConsole,
/// lo mismo con la consola, que es una fuente de entrada y se arma después (etapa 8b); sin ella, lights da console_disconnected.
/// ToggleOverlay es Dispatcher.ToggleOverlay, por lo mismo que GoToPage (plan de la 1.3.0); sin ella, page.overlay no hace nada.
/// </summary>
public sealed record Chains(
    Library Library,
    IWindows Windows,
    ScriptRunner Scripts,
    Func<string, Failure?> GoToPage,
    ScriptOutputSink? Output = null,
    Func<string, CancellationToken, Task<Result>>? SendToConsole = null,
    Func<string, Failure?>? ToggleOverlay = null);

/// <summary>
/// El menú fijo de pasos (Decidido 8): una línea por tipo, y cada uno recibe por constructor sólo lo que usa. La marca va primera.
/// Un tipo nuevo es un archivo en Steps/ y una línea acá (01 §6).
/// </summary>
public static class Catalog
{
    public static IReadOnlyList<IStepType> Build(ObsClient obs, StateStore state, Log log, TimeProvider clock, Chains chains)
    {
        ArgumentNullException.ThrowIfNull(chains);
        return
        [
            new ObsMarker(obs, state),
            new ObsScene(obs, state, log),
            new ObsRecord(obs, state, log, clock),
            new ObsMute(obs, state, clock),
            new ObsRequestStep(obs, state),
            new WaitTime(clock),
            new WaitWindow(chains.Windows, clock),
            new ScriptRun(chains.Scripts, chains.Library, chains.Output),
            new PageGo(chains.Library, chains.GoToPage),
            new PageOverlay(chains.Library, chains.ToggleOverlay ?? (_ => null)),
            new Lights(chains.SendToConsole),
        ];
    }
}
