using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public sealed class NotifierTests : IAsyncLifetime
{
    private const string Config = """{ "version": 1, "actions": [ { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] } ]CONSOLE }""";

    private readonly TempFolder _temp = new();
    private readonly StateStore _state = new();
    private readonly List<Notice> _sounds = [];
    private readonly List<Notice> _balloons = [];
    private Log _log = null!;

    public Task InitializeAsync()
    {
        _log = new Log(_temp.Combine("log"), TimeProvider.System);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _state.DisposeAsync();
        await _log.DisposeAsync();
        _temp.Dispose();
    }

    private Notifier NewNotifier(RecordingState recording, bool streaming = false, string? console = null)
    {
        Directory.CreateDirectory(_temp.Combine("biblioteca"));
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), Config.Replace("CONSOLE", console is null ? "" : $", \"console\": {console}", StringComparison.Ordinal));
        var library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        Assert.Empty(library.Load());
        _state.Update(s => s with { Obs = s.Obs with { Link = ObsLinkState.Ready, Recording = recording, Streaming = streaming } });
        return new Notifier(_state, library, _log, _sounds.Add, _balloons.Add);
    }

    private static ActionResult MarkerFailed(string code, string message) => new(
        "marca-short", Guid.NewGuid(), Trigger.Hotkey, false, 1, ObsMarker.TypeId, new Failure(code, message, "steps[0]"), TimeSpan.Zero, [new StepReport(StepOutcome.Failed)]);

    private static ActionResult Succeeded() => new(
        "marca-short", Guid.NewGuid(), Trigger.Hotkey, true, null, null, null, TimeSpan.Zero, [new StepReport(StepOutcome.Ok)]);

    [Theory]
    [InlineData(RecordingState.Stopped, false, true, true, true)]
    [InlineData(RecordingState.Stopped, false, false, true, true)]
    [InlineData(RecordingState.Recording, false, true, false, false)]
    [InlineData(RecordingState.Paused, false, true, false, false)]
    [InlineData(RecordingState.Starting, false, true, false, false)]
    [InlineData(RecordingState.Stopping, false, true, false, false)]
    [InlineData(RecordingState.Stopped, true, true, false, false)]
    [InlineData(RecordingState.Paused, false, false, true, false)]
    [InlineData(RecordingState.Stopped, true, false, true, false)]
    public void The_rule_of_the_matrix(RecordingState recording, bool streaming, bool mute, bool sound, bool balloon)
    {
        Assert.Equal(new NoticeChannels(sound, balloon), Notifier.Decide(recording, streaming, mute));
    }

    [Fact]
    public void A_marker_that_does_not_go_in_while_paused_shows_only_the_icon()
    {
        Notifier notifier = NewNotifier(RecordingState.Paused);

        notifier.OnFinished(MarkerFailed(ErrorCodes.MarkerPaused, "Estás en pausa: la marca no entró."));

        Assert.Empty(_sounds);
        Assert.Empty(_balloons);
        Assert.Equal(
            new ActionFailure("marker_failed", "Falló Marca: short: paso 1 (obs.marker)", "Estás en pausa: la marca no entró."),
            _state.Current.LastFailure);
        Assert.Contains(_log.Recent(), entry => entry.Module == "notifier" && entry.Code == ErrorCodes.MarkerPaused);
    }

    [Fact]
    public void Stopped_it_sounds_and_shows_the_balloon_once()
    {
        Notifier notifier = NewNotifier(RecordingState.Stopped);

        notifier.OnFinished(MarkerFailed(ErrorCodes.MarkerNotRecording, "No estás grabando: la marca no entró."));

        var expected = new Notice("marker_failed", "Falló Marca: short: paso 1 (obs.marker)", "No estás grabando: la marca no entró.");
        Assert.Equal(expected, Assert.Single(_sounds));
        Assert.Equal(expected, Assert.Single(_balloons));
        Assert.NotNull(_state.Current.LastFailure);
    }

    [Fact]
    public void With_the_mute_rule_off_it_sounds_during_a_take_but_there_is_still_no_balloon()
    {
        Notifier notifier = NewNotifier(RecordingState.Paused, console: """{ "muteSoundWhileRecording": false }""");

        notifier.OnFinished(MarkerFailed(ErrorCodes.MarkerPaused, "Estás en pausa: la marca no entró."));

        Assert.Single(_sounds);
        Assert.Empty(_balloons);
    }

    [Fact]
    public void Streaming_counts_as_a_take_in_progress()
    {
        Notifier notifier = NewNotifier(RecordingState.Stopped, streaming: true);

        notifier.OnFinished(MarkerFailed(ErrorCodes.MarkerNotRecording, "No estás grabando: la marca no entró."));

        Assert.Empty(_sounds);
        Assert.Empty(_balloons);
        Assert.NotNull(_state.Current.LastFailure);
    }

    [Fact]
    public void A_failure_that_is_not_of_a_marker_is_an_action_failed()
    {
        Notifier notifier = NewNotifier(RecordingState.Stopped);
        var missing = new ActionResult("marca-inicoi", Guid.NewGuid(), Trigger.Hotkey, false, null, null, new Failure(ErrorCodes.ConfigInvalid, "La acción \"marca-inicoi\" no existe."), TimeSpan.Zero, []);

        notifier.OnFinished(missing);

        Assert.Equal(new ActionFailure("action_failed", "Falló marca-inicoi", "La acción \"marca-inicoi\" no existe."), _state.Current.LastFailure);
        Assert.Equal("action_failed", Assert.Single(_balloons).Id);
    }

    [Fact]
    public void The_next_action_that_ends_well_clears_the_failure()
    {
        Notifier notifier = NewNotifier(RecordingState.Paused);
        notifier.OnFinished(MarkerFailed(ErrorCodes.MarkerPaused, "Estás en pausa: la marca no entró."));

        notifier.OnFinished(Succeeded());

        Assert.Null(_state.Current.LastFailure);
        Assert.Empty(_sounds);
    }

    [Fact]
    public void Opening_the_menu_clears_the_failure()
    {
        Notifier notifier = NewNotifier(RecordingState.Paused);
        notifier.OnFinished(MarkerFailed(ErrorCodes.MarkerPaused, "Estás en pausa: la marca no entró."));

        notifier.Dismiss();

        Assert.Null(_state.Current.LastFailure);
    }
}
