using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>
/// El catálogo de pasos y el de condiciones reales, armados para una prueba: el OBS no se conecta (puerto cerrado), las ventanas son de
/// mentira y page.go no hace nada. Sirve para probar lo que depende de los campos y de las reglas de cada tipo sin inventar un tipo falso.
/// </summary>
internal sealed class RealCatalog : IAsyncDisposable
{
    private readonly TempFolder _temp = new();

    public RealCatalog(CoreLibrary? library = null, IWindows? windows = null)
    {
        State = new StateStore();
        Log = new Log(_temp.Combine("log"), TimeProvider.System);
        Obs = new ObsClient(() => new Uri("ws://127.0.0.1:9"), () => null, State, Log, TimeProvider.System);
        Library = library ?? new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        Chains chains = TestChains.For(Library, Log, windows);
        Steps = Catalog.Build(Obs, State, Log, TimeProvider.System, chains);
        Conditions = PampaPad.Core.Conditions.Build(chains.Windows, State, Obs);
        Executor = new Executor(Steps, Conditions, TimeProvider.System);
    }

    public StateStore State { get; }

    public Log Log { get; }

    public ObsClient Obs { get; }

    public CoreLibrary Library { get; }

    public IReadOnlyList<IStepType> Steps { get; }

    public IReadOnlyList<ConditionType> Conditions { get; }

    public Executor Executor { get; }

    public async ValueTask DisposeAsync()
    {
        await Obs.DisposeAsync();
        await State.DisposeAsync();
        await Log.DisposeAsync();
        _temp.Dispose();
    }
}
