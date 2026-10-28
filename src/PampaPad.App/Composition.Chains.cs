using PampaPad.Core;

namespace PampaPad.App;

// Las cadenas de la etapa 5, aparte por el tope de 300 líneas (01 §1).
internal sealed partial class Composition
{
    /// <summary>Los tipos de paso que corre el Executor. La prueba de humo los busca en la foto que manda el puente (SnapshotCheck).</summary>
    public IReadOnlyList<IStepType> StepTypes { get; private set; } = [];

    /// <summary>Los tipos de condición que corre el Executor, para el mismo control de la prueba de humo.</summary>
    public IReadOnlyList<ConditionType> ConditionTypes { get; private set; } = [];

    /// <summary>
    /// ScriptRunner, condiciones, Catalog y Executor, en el orden de 01 §4.3. page.go cambia la página con el Dispatcher, que se arma
    /// después: lo recibe como función. A los scripts se les pasa la raíz de Conocimiento aunque el entorno del programa no la tenga
    /// (02 §9, punto 3): se lee también la del usuario, como en Program.cs.
    /// </summary>
    private static (IReadOnlyList<IStepType> Catalog, IReadOnlyList<ConditionType> Conditions, Executor Executor, ScriptRunner Scripts) ComposeChains(
        Library library, StateStore state, ObsClient obs, IWindows windows, Log log, TimeProvider clock, Func<string, Failure?> goToPage, ScriptOutputSink output)
    {
        string? root = DataRoot.Read(name =>
            Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User));
        var scripts = new ScriptRunner(library, log, clock, root);
        IReadOnlyList<ConditionType> conditions = Conditions.Build(windows, state, obs);
        IReadOnlyList<IStepType> catalog = Catalog.Build(obs, state, log, clock, new Chains(library, windows, scripts, goToPage, output));
        return (catalog, conditions, new Executor(catalog, conditions, clock), scripts);
    }

    /// <summary>
    /// Los dos ítems de la bandeja de la etapa 5 (05 §3). Cancelar sale del hilo de la interfaz: puede seguir la cadena ahí mismo. Como
    /// los demás manejadores del menú, ninguno deja escapar una excepción sin anotarla.
    /// </summary>
    private void HookChains(Tray tray, Preflight preflight, Notifier notifier, Dispatcher dispatcher)
    {
        tray.PreflightRequested += () => _ = RunPreflightAsync(preflight, notifier);
        tray.CancelAllRequested += () => _ = Task.Run(() =>
        {
            try
            {
                Log.Info("app", "Cancelar todo, desde la bandeja.");
                dispatcher.CancelAll();
            }
            catch (Exception ex)
            {
                Log.Error("app", "Falló cancelar todo: " + ex.Message, ErrorCodes.InternalError);
            }
        });
    }

    // "Chequeo previo" de la bandeja: una línea por comprobación en el registro y el globo con el resumen (01 §8, fila 5).
    private async Task RunPreflightAsync(Preflight preflight, Notifier notifier)
    {
        try
        {
            PreflightReport report = await preflight.RunAsync(Trigger.Tray, _life.Token).ConfigureAwait(false);
            notifier.OnPreflight(report);
        }
        catch (Exception ex)
        {
            Log.Error("app", "Falló el chequeo previo: " + ex, ErrorCodes.InternalError);
        }
    }
}
