using PampaPad.Core;

namespace PampaPad.App;

// Las cadenas de la etapa 5, aparte por el tope de 300 líneas (01 §1).
internal sealed partial class Composition
{
    /// <summary>
    /// ScriptRunner, condiciones, Catalog y Executor, en el orden de 01 §4.3. page.go cambia la página con el Dispatcher, que se arma
    /// después: lo recibe como función. A los scripts se les pasa la raíz de Conocimiento aunque el entorno del programa no la tenga
    /// (02 §9, punto 3): se lee también la del usuario, como en Program.cs.
    /// </summary>
    private static (IReadOnlyList<IStepType> Catalog, IReadOnlyList<ConditionType> Conditions, Executor Executor) ComposeChains(
        Library library, StateStore state, ObsClient obs, IWindows windows, Log log, TimeProvider clock, Func<string, Failure?> goToPage)
    {
        string? root = DataRoot.Read(name =>
            Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User));
        var scripts = new ScriptRunner(library, log, clock, root);
        IReadOnlyList<ConditionType> conditions = Conditions.Build(windows, state, obs);
        IReadOnlyList<IStepType> catalog = Catalog.Build(obs, state, log, clock, new Chains(library, windows, scripts, goToPage));
        return (catalog, conditions, new Executor(catalog, conditions, clock));
    }
}
