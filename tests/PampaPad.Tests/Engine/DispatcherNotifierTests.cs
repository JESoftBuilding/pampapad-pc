using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El avisador enganchado a Finished como lo engancha Composition: de la pulsación al ícono, al sonido y al cartel.</summary>
public class DispatcherNotifierTests
{
    private const string Short = DispatcherRig.Short;

    private static Notifier Hook(DispatcherRig rig, List<Notice> sounds, List<Notice> balloons)
    {
        var notifier = new Notifier(rig.State, rig.Library, rig.Log, sounds.Add, balloons.Add);
        rig.Dispatcher.Finished += notifier.OnFinished;
        return notifier;
    }

    [Fact]
    public async Task A_marker_that_fails_on_a_press_shows_up_everywhere_with_the_name_of_the_key()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        var (sounds, balloons) = (new List<Notice>(), new List<Notice>());
        Hook(rig, sounds, balloons);
        rig.Marker.Behavior = (_, _, _) =>
            Task.FromResult(new StepResult(new Failure(ErrorCodes.MarkerNotRecording, "No estás grabando: la marca no entró.")));

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.State.Current.LastFailure is not null);

        Assert.Equal(
            new ActionFailure("marker_failed", "Falló Marca: short: paso 1 (obs.marker)", "No estás grabando: la marca no entró."),
            rig.State.Current.LastFailure);
        Assert.Single(sounds);
        Assert.Single(balloons);
        Assert.Contains("Falló Marca: short", TraySummary.From(rig.State.Current).Tooltip, StringComparison.Ordinal);
        Assert.Equal(TrayIcon.ActionFailed, TraySummary.From(rig.State.Current).Icon);
    }

    [Fact]
    public async Task The_next_press_that_goes_in_takes_the_icon_away()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        var (sounds, balloons) = (new List<Notice>(), new List<Notice>());
        Hook(rig, sounds, balloons);
        rig.Marker.Behavior = (_, _, _) =>
            Task.FromResult(new StepResult(new Failure(ErrorCodes.MarkerNotRecording, "No estás grabando: la marca no entró.")));
        rig.Windows.Press(Short);
        await Poll.Until(() => rig.State.Current.LastFailure is not null);

        rig.Marker.Behavior = (_, _, _) => Task.FromResult(new StepResult(null));
        rig.Windows.Press(DispatcherRig.Inicio);
        await Poll.Until(() => rig.State.Current.LastFailure is null);

        Assert.Single(sounds);
        Assert.Single(balloons);
    }

    [Fact]
    public async Task A_key_whose_action_does_not_exist_is_an_action_failed_by_name()
    {
        string broken = DispatcherRig.Example.Replace("\"action\": \"marca-inicio\"", "\"action\": \"marca-inicoi\"", StringComparison.Ordinal);
        await using var rig = new DispatcherRig(broken);
        var (sounds, balloons) = (new List<Notice>(), new List<Notice>());
        Hook(rig, sounds, balloons);

        rig.Windows.Press(DispatcherRig.Inicio);
        await Poll.Until(() => rig.State.Current.LastFailure is not null);

        Assert.Equal(
            new ActionFailure("action_failed", "Falló marca-inicoi", "La acción \"marca-inicoi\" no existe."),
            rig.State.Current.LastFailure);
        Assert.Single(balloons);
    }

    [Fact]
    public async Task A_press_that_is_ignored_because_the_action_is_running_says_nothing()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        var (sounds, balloons) = (new List<Notice>(), new List<Notice>());
        Hook(rig, sounds, balloons);
        System.Action release = rig.Hold();

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.State.Current.Running.Count == 1);
        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Log.Recent().Any(entry => entry.Message.Contains("ya está en curso", StringComparison.Ordinal)));
        release();
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Null(rig.State.Current.LastFailure);
        Assert.Empty(sounds);
        Assert.Empty(balloons);
    }

    [Fact]
    public async Task A_notifier_that_throws_does_not_stop_the_next_press()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        Hook(rig, [], []);
        rig.Dispatcher.Finished += _ => throw new InvalidOperationException("roto a propósito");
        rig.Marker.Behavior = (_, _, _) =>
            Task.FromResult(new StepResult(new Failure(ErrorCodes.MarkerNotRecording, "No estás grabando: la marca no entró.")));

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 1);
        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 2);

        Assert.NotNull(rig.State.Current.LastFailure);
    }
}
