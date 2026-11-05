using System.Text.Json;
using PampaPad.Core;
using static PampaPad.Tests.AlertRun;

namespace PampaPad.Tests;

/// <summary>obs_lost, recording_with_failure y marker_failed, la prioridad, el clic, los canales y el largo de los textos (plan de la 9b, E9b-3).</summary>
public class GraveAlertsOthersTests
{
    private static readonly AppState Stopped = Obs();

    private static AppState Link(AppState state, ObsLinkState link) => state with { Obs = state.Obs.WithLink(link) };

    [Fact]
    public void Obs_closing_while_recording_says_it_closed_with_what_was_recorded_even_after_closed_clears_the_recording()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        AppState recording = Obs(RecordingState.Recording, recorded: new TimeSpan(0, 34, 12));
        run.Look(recording);

        GraveAlert? exiting = run.Look(Link(recording, ObsLinkState.Exiting));
        AppState closed = Link(Link(recording, ObsLinkState.Exiting), ObsLinkState.Closed);
        Assert.Equal(RecordingState.Stopped, closed.Obs.Recording);
        GraveAlert? after = run.After(TimeSpan.FromSeconds(30), closed);

        Assert.Equal(new GraveAlert(AlertTexts.ObsLost, 5, 2, "SIN OBS", "OBS SE CERRO", "0:34:12 GRABADOS", "CLICK: YA SE", T0, true, true), exiting);
        Assert.Equal(exiting, after);
        Assert.Null(run.Click(closed));
        Assert.Null(run.After(TimeSpan.FromSeconds(30), closed));
    }

    [Fact]
    public void A_lost_connection_says_there_is_no_data_and_obs_coming_back_with_the_take_turns_it_off()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        AppState paused = Obs(RecordingState.Paused, recorded: TimeSpan.FromSeconds(75));
        run.Look(paused);

        Assert.Equal("SIN DATOS DE OBS", run.Look(Link(paused, ObsLinkState.Lost))?.Line1);
        Assert.Equal("0:01:15 GRABADOS", run.Memory.Shown?.Line2);
        Assert.Null(run.Look(paused));
    }

    [Fact]
    public void Obs_going_away_while_stopped_or_before_it_was_ever_ready_does_not_alert()
    {
        var run = new AlertRun();
        Assert.Null(run.Look(Link(Obs(RecordingState.Recording), ObsLinkState.Lost)));

        run.Look(Stopped);
        Assert.Null(run.Look(Link(Stopped, ObsLinkState.Exiting)));
    }

    [Fact]
    public void Obs_back_and_stopped_keeps_obs_lost_until_the_click()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        AppState recording = Obs(RecordingState.Recording);
        run.Look(recording);
        run.Look(Link(recording, ObsLinkState.Closed));

        Assert.Equal(AlertTexts.ObsLost, run.Look(Stopped)?.Id);
        Assert.Null(run.Click(Stopped));
    }

    [Fact]
    public void Recording_with_failure_shows_the_failure_of_the_check_and_goes_away_with_a_click_or_stopping()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        AppState recording = Obs(RecordingState.Recording);
        run.Look(recording);
        run.Memory = run.Memory with { StartFailure = "CAMARA TAPADA" };

        Assert.Equal(new GraveAlert(AlertTexts.RecordingWithFailure, 6, 1, "OJO", "GRABANDO CON:", "CAMARA TAPADA", "CLICK: YA SE", T0, true, true), run.Look(recording));
        Assert.Null(run.Click(recording));

        run.Look(Stopped);
        run.Look(recording);
        run.Memory = run.Memory with { StartFailure = "CAMARA TAPADA" };
        Assert.NotNull(run.Look(recording));
        Assert.Null(run.Look(Stopped));
    }

    [Theory]
    [InlineData(ErrorCodes.MarkerPaused, "ESTAS EN PAUSA", "", "SEGUI Y MARCA")]
    [InlineData(ErrorCodes.MarkerNotRecording, "NO ESTAS GRABANDO", "", "SEGUI Y MARCA")]
    [InlineData(ErrorCodes.MarkerRejected, "OBS RECHAZO", "LA MARCA", "")]
    [InlineData(ErrorCodes.MarkerUnsupported, "ESTE OBS NO", "ADMITE MARCAS", "")]
    [InlineData(ErrorCodes.ObsDisconnected, "SIN OBS", "", "")]
    public void Marker_failed_says_why_from_the_code_and_goes_away_alone_at_two_seconds(string code, string line1, string line2, string hint)
    {
        var run = new AlertRun();
        AppState failed = Stopped with { RecentFailure = new RecentFailure(Notifier.MarkerFailed, "Marca: short", 1, T0, code) };

        Assert.Equal(new GraveAlert(AlertTexts.MarkerFailed, 1, 1, "NO ENTRO", line1, line2, hint, T0, true, true), run.Look(failed));
        Assert.NotNull(run.After(TimeSpan.FromMilliseconds(1999), failed));
        Assert.Null(run.After(TimeSpan.FromMilliseconds(1), failed));
    }

    [Fact]
    public void An_action_that_failed_is_not_a_grave_alert_and_a_clicked_marker_does_not_come_back_but_a_new_one_does()
    {
        var run = new AlertRun();
        Assert.Null(run.Look(Stopped with { RecentFailure = new RecentFailure(Notifier.ActionFailed, "Banco", 2, T0, ErrorCodes.ObsTimeout) }));

        AppState first = Stopped with { RecentFailure = new RecentFailure(Notifier.MarkerFailed, "Marca", 1, T0, ErrorCodes.MarkerPaused) };
        run.Look(first);
        Assert.Null(run.Click(first));
        Assert.Null(run.After(TimeSpan.FromMilliseconds(500), first));

        AppState second = Stopped with { RecentFailure = new RecentFailure(Notifier.MarkerFailed, "Marca", 1, run.Now, ErrorCodes.MarkerPaused) };
        Assert.NotNull(run.Look(second));
    }

    [Fact]
    public void With_two_alerts_the_first_by_priority_shows_and_the_other_comes_out_after_the_click()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        run.Look(Obs(RecordingState.Recording, micMuted: true));
        AppState pausedMuted = Obs(RecordingState.Paused, micMuted: true);

        Assert.Equal(AlertTexts.MicMuted, run.After(TimeSpan.FromMinutes(4), pausedMuted)?.Id);
        Assert.Equal(AlertTexts.LongPause, run.Click(pausedMuted)?.Id);
        Assert.Equal(AlertTexts.ObsLost, run.Look(Link(pausedMuted, ObsLinkState.Lost))?.Id);
    }

    [Fact]
    public void A_click_without_an_alert_does_nothing()
    {
        var run = new AlertRun();
        run.Look(Obs(RecordingState.Recording));
        AlertMemory before = run.Memory;

        Assert.Same(before, GraveAlerts.Attend(before));
    }

    // Decisión 9: el zumbador sigue el canal del aviso y no la regla de sonido; los canales quedan fijos mientras el aviso dura.
    [Fact]
    public void Light_and_sound_come_from_console_alerts_when_it_appears_and_ignore_the_take()
    {
        var run = new AlertRun { Config = Parse("""{ "version": 1, "console": { "muteSoundWhileRecording": true, "alerts": { "mic_muted": { "light": false, "sound": false } } } }""") };
        run.Look(Stopped);
        AppState muted = Obs(RecordingState.Recording, micMuted: true, recorded: TimeSpan.FromHours(3));

        GraveAlert? mic = run.Look(muted);
        run.Config = new Config();
        GraveAlert? still = run.Click(muted);

        Assert.Equal((false, false), (mic!.Light, mic.Sound));
        Assert.Equal(AlertTexts.StillRecording, still?.Id);
        Assert.Equal((true, true), (still!.Light, still.Sound));
    }

    [Fact]
    public void Changing_console_alerts_while_an_alert_lasts_does_not_change_its_light_or_sound()
    {
        var run = new AlertRun();
        run.Look(Stopped);
        AppState muted = Obs(RecordingState.Recording, micMuted: true);
        run.Look(muted);

        run.Config = Parse("""{ "version": 1, "console": { "alerts": { "mic_muted": { "light": false, "sound": false } } } }""");
        GraveAlert? still = run.After(TimeSpan.FromSeconds(1), muted);

        Assert.Equal(AlertTexts.MicMuted, still?.Id);
        Assert.Equal((true, true), (still!.Light, still.Sound));
    }

    [Fact]
    public void Every_text_fits_in_its_line()
    {
        var run = new AlertRun();
        var seen = new List<GraveAlert>();
        void Add(GraveAlert? alert) => seen.AddRange(alert is null ? [] : [alert]);
        run.Look(Stopped);
        Add(run.Look(Obs(RecordingState.Recording, micMuted: true, recorded: TimeSpan.FromHours(123))));
        Add(run.Click(Obs(RecordingState.Recording, micMuted: true, recorded: TimeSpan.FromHours(123))));
        run.Memory = run.Memory with { StartFailure = AlertTexts.Failure(new CheckResult("test.otra", "Una comprobación con un mensaje larguísimo", ConditionOutcome.Fail, "")) };
        Add(run.Click(Obs(RecordingState.Recording, recorded: TimeSpan.FromHours(123))));
        Add(run.After(TimeSpan.FromHours(10), Obs(RecordingState.Paused, recorded: TimeSpan.FromHours(123))));
        Add(run.Look(Link(Obs(RecordingState.Paused, recorded: TimeSpan.FromHours(123)), ObsLinkState.Lost)));
        foreach (string code in new[] { ErrorCodes.MarkerPaused, ErrorCodes.MarkerNotRecording, ErrorCodes.MarkerRejected, ErrorCodes.MarkerUnsupported, ErrorCodes.ObsDisconnected, ErrorCodes.ObsTimeout, ErrorCodes.Cancelled, "otro" })
        {
            Add(new AlertRun().Look(Stopped with { RecentFailure = new RecentFailure(Notifier.MarkerFailed, "M", 1, T0, code) }));
        }

        Assert.Equal(13, seen.Count);
        Assert.All(seen, alert =>
        {
            Assert.InRange(alert.Title.Length, 1, AlertTexts.TitleLetters);
            Assert.InRange(alert.Line1.Length, 1, AlertTexts.LineLetters);
            Assert.InRange(alert.Line2.Length, 0, AlertTexts.LineLetters);
            Assert.InRange(alert.Hint.Length, 0, AlertTexts.HintLetters);
            Assert.All(alert.Title + alert.Line1 + alert.Line2 + alert.Hint, c => Assert.InRange(c, ' ', 'Z'));
        });
    }

    private static Config Parse(string json) => JsonSerializer.Deserialize<Config>(json, Json.Options)!;
}
