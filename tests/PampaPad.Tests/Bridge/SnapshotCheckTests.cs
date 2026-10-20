using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// El control que la prueba de humo le aplica al puente del programa real (revisión de E5-23a): app.snapshot, pedido como lo pide la
/// ventana, trae los mismos tipos de paso y de condición que corre el Executor. Composition no se ve desde acá: estas pruebas arman el
/// puente a mano, con las listas que Composition podría pasarle mal.
/// </summary>
public sealed class SnapshotCheckTests : IAsyncLifetime
{
    private readonly DispatcherRig _rig = new(DispatcherRig.Example);
    private readonly ConditionType _window = FakeCondition.Met(Conditions.WindowExists).Type;
    private readonly ConditionType _recording = FakeCondition.Met(Conditions.RecordingIs).Type;
    private readonly List<IDisposable> _hooks = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _hooks.ForEach(hook => hook.Dispose());
        await _rig.DisposeAsync();
    }

    [Fact]
    public async Task A_snapshot_with_the_types_the_program_runs_has_no_problem()
    {
        Bridge bridge = Compose([_rig.Marker], [_window, _recording]);

        Assert.Null(await Check(bridge, [_rig.Marker.Id], [_recording.Id, _window.Id]));
    }

    // La mutación de la revisión: ComposeBridge con [] en lugar de conditions compilaba y dejaba la suite entera en verde.
    [Fact]
    public async Task A_bridge_composed_without_the_conditions_is_a_problem()
    {
        Bridge bridge = Compose([_rig.Marker], []);

        string? problem = await Check(bridge, [_rig.Marker.Id], [_window.Id, _recording.Id]);

        Assert.NotNull(problem);
        Assert.Contains("conditionTypes", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bridge_with_other_step_types_is_a_problem()
    {
        Bridge bridge = Compose([new FakeStep("obs.scene")], [_window]);

        string? problem = await Check(bridge, [_rig.Marker.Id], [_window.Id]);

        Assert.NotNull(problem);
        Assert.Contains("stepTypes", problem, StringComparison.Ordinal);
    }

    // Si ComposeChains devolviera listas vacías, la foto y lo que corre coincidirían igual: vacía no cuenta como igual.
    [Fact]
    public async Task Empty_lists_on_both_sides_are_a_problem()
    {
        Bridge bridge = Compose([], []);

        Assert.NotNull(await Check(bridge, [], []));
    }

    [Fact]
    public async Task A_bridge_without_app_snapshot_is_a_problem()
    {
        var bridge = new Bridge(_rig.State, _rig.Log);

        Assert.NotNull(await Check(bridge, [_rig.Marker.Id], [_window.Id]));
    }

    private static Task<string?> Check(Bridge bridge, string[] stepTypes, string[] conditionTypes) =>
        SnapshotCheck.ProblemAsync(bridge, stepTypes, conditionTypes, CancellationToken.None);

    private Bridge Compose(IReadOnlyList<IStepType> catalog, IReadOnlyList<ConditionType> conditions)
    {
        var bridge = new Bridge(_rig.State, _rig.Log);
        _hooks.Add(Requests.RegisterAll(bridge, new StatusSources(
            "0.0.0", _rig.State, _rig.Library, _rig.Dispatcher, _rig.Log, catalog, conditions, () => new SettingsView(false, null, null))));
        return bridge;
    }
}
