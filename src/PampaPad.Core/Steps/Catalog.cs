namespace PampaPad.Core;

/// <summary>
/// Lo que necesitan los pasos de cadenas, además de OBS: la biblioteca (scripts y páginas), las ventanas, quien corre los scripts y quien
/// cambia de página. GoToPage es Dispatcher.Go, que se arma después del catálogo (01 §4.3): por eso llega como función.
/// </summary>
public sealed record Chains(Library Library, IWindows Windows, ScriptRunner Scripts, Func<string, Failure?> GoToPage);

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
            new ObsMute(obs, state),
            new ObsRequestStep(obs, state),
            new WaitTime(clock),
            new WaitWindow(chains.Windows, clock),
            new ScriptRun(chains.Scripts, chains.Library),
        ];
    }
}
