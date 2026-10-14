using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que queda anotado de cada corrida y cómo se libera la acción cuando algo sale mal, se cancela o se pide muchas veces a la vez.</summary>
public class DispatcherRunTests
{
    private const string Short = DispatcherRig.Short;
    private const string Inicio = DispatcherRig.Inicio;

    [Fact]
    public async Task Finished_never_comes_out_of_the_thread_of_the_source()
    {
        // La acción de esta tecla no existe: es el camino que no pasa por el grupo de hilos por su cuenta.
        string broken = DispatcherRig.Example.Replace("\"action\": \"marca-inicio\"", "\"action\": \"marca-inicoi\"", StringComparison.Ordinal);
        await using var rig = new DispatcherRig(broken);
        using var stuck = new ManualResetEventSlim();
        rig.Dispatcher.Finished += _ => stuck.Wait();

        Task press = Task.Run(() => rig.Windows.Press(Inicio));
        Task winner = await Task.WhenAny(press, Task.Delay(TimeSpan.FromSeconds(3)));
        stuck.Set();

        Assert.Same(press, winner);
    }

    [Fact]
    public async Task A_failed_run_leaves_one_line_with_the_step_the_code_and_who_asked()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        rig.Marker.Behavior = (_, _, _) =>
            Task.FromResult(new StepResult(new Failure(ErrorCodes.MarkerNotRecording, "No estás grabando: la marca no entró.")));

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 1);

        ActionResult result = Assert.Single(rig.Finished);
        LogEntry line = Assert.Single(rig.Log.Recent(), entry => entry.RunId == result.RunId);
        Assert.False(result.Ok);
        Assert.Equal((1, "obs.marker"), (result.StepNumber, result.StepType));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal(ErrorCodes.MarkerNotRecording, line.Code);
        Assert.Equal(Trigger.Hotkey, line.RequestedBy);
        Assert.Contains("marca-short", line.Message, StringComparison.Ordinal);
        Assert.Contains("paso 1 (obs.marker)", line.Message, StringComparison.Ordinal);
        Assert.Contains("No estás grabando: la marca no entró.", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_that_ends_well_leaves_one_info_line_with_its_run()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 1);

        ActionResult result = Assert.Single(rig.Finished);
        LogEntry line = Assert.Single(rig.Log.Recent(), entry => entry.RunId == result.RunId);
        Assert.Equal(LogLevel.Info, line.Level);
        Assert.Null(line.Code);
        Assert.Equal(Trigger.Hotkey, line.RequestedBy);
        Assert.Contains("marca-short terminó bien", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_that_throws_is_an_internal_error_and_the_action_can_run_again()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        rig.Marker.Behavior = (_, _, _) => throw new InvalidOperationException("roto a propósito");

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 1);
        await Poll.Until(() => rig.State.Current.Running.Count == 0);
        rig.Marker.Behavior = (_, _, _) => Task.FromResult(new StepResult(null));
        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 2);

        ActionResult[] results = [.. rig.Finished];
        Assert.Equal(ErrorCodes.InternalError, results[0].Failure?.Code);
        Assert.True(results[1].Ok);
    }

    [Fact]
    public async Task A_cancelled_run_frees_the_action_for_the_next_press()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        rig.Marker.Behavior = async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new StepResult(null);
        };

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.State.Current.Running.Count == 1);
        Assert.True(rig.Dispatcher.Cancel(rig.State.Current.Running[0].RunId));
        await Poll.Until(() => rig.Finished.Count == 1);
        await Poll.Until(() => rig.State.Current.Running.Count == 0);
        rig.Marker.Behavior = (_, _, _) => Task.FromResult(new StepResult(null));
        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 2);

        ActionResult[] results = [.. rig.Finished];
        Assert.Equal(ErrorCodes.Cancelled, results[0].Failure?.Code);
        Assert.True(results[1].Ok);
    }

    [Fact]
    public async Task Cancelling_with_nothing_running_does_nothing()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);

        rig.Dispatcher.CancelAll();

        Assert.False(rig.Dispatcher.Cancel(Guid.Empty));
        Assert.Empty(rig.Finished);
    }

    [Fact]
    public async Task The_same_action_launched_from_many_threads_runs_once()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        System.Action release = rig.Hold();

        Task<ActionResult>[] launches = [.. Enumerable.Range(0, 32).Select(_ =>
            Task.Run(() => rig.Dispatcher.Launch("marca-short", new Invocation(Guid.NewGuid(), Trigger.Tray, null, null, null, null))))];
        await Poll.Until(() => launches.Count(launch => launch.IsCompleted) == 31);
        release();
        ActionResult[] results = await Task.WhenAll(launches);

        Assert.Single(rig.Marker.Runs);
        Assert.Single(results, result => result.Ok);
        Assert.Equal(31, results.Count(result => result.Failure?.Code == ErrorCodes.Cancelled));
        Assert.Single(rig.Finished);
    }

    [Fact]
    public async Task While_an_action_runs_the_program_cannot_restart_and_afterwards_it_can()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        System.Action release = rig.Hold();

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.State.Current.Running.Count == 1);
        RestartVerdict during = RestartPolicy.Evaluate(rig.State.Current);
        release();
        await Poll.Until(() => rig.State.Current.Running.Count == 0);

        Assert.False(during.Allowed);
        Assert.Equal("Hay una acción en curso", during.Reason);
        Assert.True(RestartPolicy.Evaluate(rig.State.Current).Allowed);
    }

    [Fact]
    public async Task The_running_list_says_which_action_and_who_asked()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        System.Action release = rig.Hold();

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.State.Current.Running.Count == 1);
        RunningAction running = rig.State.Current.Running[0];
        release();
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Equal(("marca-short", Trigger.Hotkey), (running.ActionId, running.RequestedBy));
        Assert.Equal(Assert.Single(rig.Finished).RunId, running.RunId);
    }

    [Fact]
    public async Task Launching_an_action_that_does_not_exist_fails_with_its_name_and_runs_nothing()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        var invocation = new Invocation(Guid.NewGuid(), Trigger.Mcp, null, null, null, null);

        ActionResult result = await rig.Dispatcher.Launch("no-existe", invocation);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Contains("\"no-existe\"", result.Failure?.Message, StringComparison.Ordinal);
        Assert.Equal((invocation.RunId, Trigger.Mcp), (result.RunId, result.RequestedBy));
        Assert.Empty(rig.Marker.Runs);
        Assert.Empty(rig.State.Current.Running);
        Assert.Equal(result, Assert.Single(rig.Finished));
    }
}
