using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que el avisador deja dicho y hecho cuando algo sale mal: la línea del registro, un canal que tira y lo que queda en el estado.</summary>
public class NotifierReportTests
{
    private const string Paused = "Estás en pausa: la marca no entró.";
    private const string NotRecording = "No estás grabando: la marca no entró.";

    [Fact]
    public async Task The_log_line_says_who_asked_the_run_the_code_and_which_channels_were_used()
    {
        await using var rig = new NotifierRig();
        ActionResult result = NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerNotRecording, NotRecording, Trigger.Key);

        rig.Notifier.OnFinished(result);

        LogEntry line = Assert.Single(rig.Log.Recent(), entry => entry.Module == "notifier");
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal(ErrorCodes.MarkerNotRecording, line.Code);
        Assert.Equal(result.RunId, line.RunId);
        Assert.Equal(Trigger.Key, line.RequestedBy);
        Assert.Contains("marker_failed", line.Message, StringComparison.Ordinal);
        Assert.Contains("Falló Marca: short: paso 1 (obs.marker)", line.Message, StringComparison.Ordinal);
        Assert.Contains(NotRecording, line.Message, StringComparison.Ordinal);
        Assert.Contains("Sonido: sí; cartel: sí.", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_log_line_says_when_nothing_but_the_icon_was_used()
    {
        await using var rig = new NotifierRig(recording: RecordingState.Paused);

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerPaused, Paused));

        LogEntry line = Assert.Single(rig.Log.Recent(), entry => entry.Module == "notifier");
        Assert.Contains("Sonido: no; cartel: no.", line.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RecordingState.Paused, """{ "muteSoundWhileRecording": false }""", "Sonido: sí; cartel: no.")]
    [InlineData(RecordingState.Paused, null, "Sonido: no; cartel: no.")]
    [InlineData(RecordingState.Stopped, null, "Sonido: sí; cartel: sí.")]
    public async Task The_log_line_names_each_channel_in_its_own_place(RecordingState recording, string? console, string channels)
    {
        await using var rig = new NotifierRig(
            console is null ? null : $$"""{ "version": 1, "actions": [ {{NotifierRig.ShortAction}} ], "console": {{console}} }""",
            recording);

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerPaused, Paused));

        LogEntry line = Assert.Single(rig.Log.Recent(), entry => entry.Module == "notifier");
        Assert.EndsWith(channels, line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sound_that_throws_does_not_stop_the_balloon_the_icon_or_the_log()
    {
        var balloons = new List<Notice>();
        await using var rig = new NotifierRig(sound: _ => throw new InvalidOperationException("sin parlantes"), balloon: balloons.Add);
        ActionResult result = NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerNotRecording, NotRecording);

        rig.Notifier.OnFinished(result);

        Assert.Single(balloons);
        Assert.NotNull(rig.State.Current.LastFailure);
        LogEntry error = Assert.Single(rig.Log.Recent(), entry => entry.Level == LogLevel.Error && entry.Module == "notifier");
        Assert.Equal(ErrorCodes.InternalError, error.Code);
        Assert.Equal(result.RunId, error.RunId);
        Assert.Contains("sonido", error.Message, StringComparison.Ordinal);
        Assert.Contains("sin parlantes", error.Message, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Recent(), entry => entry.Level == LogLevel.Warning && entry.Module == "notifier");
    }

    [Fact]
    public async Task A_balloon_that_throws_does_not_stop_the_icon_or_the_log()
    {
        var sounds = new List<Notice>();
        await using var rig = new NotifierRig(sound: sounds.Add, balloon: _ => throw new InvalidOperationException("sin bandeja"));

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerNotRecording, NotRecording));

        Assert.Single(sounds);
        Assert.NotNull(rig.State.Current.LastFailure);
        LogEntry error = Assert.Single(rig.Log.Recent(), entry => entry.Level == LogLevel.Error && entry.Module == "notifier");
        Assert.Contains("cartel", error.Message, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Recent(), entry => entry.Level == LogLevel.Warning && entry.Module == "notifier");
    }

    [Fact]
    public async Task A_new_failure_replaces_the_old_one_and_sounds_again()
    {
        await using var rig = new NotifierRig();
        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerNotRecording, NotRecording));

        rig.Notifier.OnFinished(NotifierRig.Failed("otra", 2, "wait.window", ErrorCodes.Cancelled, "Se canceló."));

        Assert.Equal(new ActionFailure("action_failed", "Falló otra: paso 2 (wait.window)", "Se canceló."), rig.State.Current.LastFailure);
        Assert.Equal(2, rig.Sounds.Count);
        Assert.Equal(2, rig.Balloons.Count);
    }

    [Fact]
    public async Task Ending_well_with_nothing_to_clear_changes_nothing_and_says_nothing()
    {
        await using var rig = new NotifierRig();
        long before = rig.State.Current.Seq;

        rig.Notifier.OnFinished(NotifierRig.Succeeded());
        rig.Notifier.Dismiss();

        Assert.Equal(before, rig.State.Current.Seq);
        Assert.Empty(rig.Sounds);
        Assert.Empty(rig.Balloons);
        Assert.DoesNotContain(rig.Log.Recent(), entry => entry.Module == "notifier");
    }

    [Fact]
    public async Task Clearing_the_failure_does_not_touch_the_rest_of_the_state()
    {
        await using var rig = new NotifierRig(recording: RecordingState.Paused);
        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerPaused, Paused));
        AppState failed = rig.State.Current;

        rig.Notifier.Dismiss();

        Assert.Equal(failed with { LastFailure = null, Seq = rig.State.Current.Seq }, rig.State.Current);
        Assert.Equal(failed.Seq + 1, rig.State.Current.Seq);
    }

    [Fact]
    public async Task Showing_the_failure_does_not_touch_the_rest_of_the_state()
    {
        await using var rig = new NotifierRig(recording: RecordingState.Paused);
        AppState before = rig.State.Current;

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerPaused, Paused));

        Assert.Equal(before with { LastFailure = rig.State.Current.LastFailure, Seq = rig.State.Current.Seq }, rig.State.Current);
    }
}
