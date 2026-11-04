using PampaPad.Core;
using static PampaPad.Tests.ProjectionCase;

namespace PampaPad.Tests;

/// <summary>La banda, los textos y la tira que salen del estado (plan de la 9a, E9-5). Las teclas y las máscaras, en ConsoleProjectionKeysTests.</summary>
public class ConsoleProjectionTests
{
    private const ScreenFlags Preflight = ScreenFlags.PreflightRan | ScreenFlags.PreflightFailed;

    [Theory]
    [InlineData(ObsLinkState.Ready, RecordingState.Stopped, ConsoleRecording.Stopped)]
    [InlineData(ObsLinkState.Ready, RecordingState.Starting, ConsoleRecording.Starting)]
    [InlineData(ObsLinkState.Ready, RecordingState.Recording, ConsoleRecording.Recording)]
    [InlineData(ObsLinkState.Ready, RecordingState.Paused, ConsoleRecording.Paused)]
    [InlineData(ObsLinkState.Ready, RecordingState.Stopping, ConsoleRecording.Stopping)]
    [InlineData(ObsLinkState.Switching, RecordingState.Recording, ConsoleRecording.Switching)]
    [InlineData(ObsLinkState.Closed, RecordingState.Stopped, ConsoleRecording.NoObs)]
    [InlineData(ObsLinkState.Loading, RecordingState.Stopped, ConsoleRecording.NoObs)]
    [InlineData(ObsLinkState.Lost, RecordingState.Recording, ConsoleRecording.NoObs)]
    public void Each_obs_state_gives_its_recording_byte(ObsLinkState link, RecordingState recording, ConsoleRecording expected)
    {
        ConsoleFrame frame = Project(State(recording, link));

        Assert.Equal(expected, frame.Screen.Recording);
        Assert.Equal((byte)expected, ConsoleFeedRig.Payload(ConsoleProtocol.ScreenStateLine(frame.Screen))[1]);
    }

    [Fact]
    public void Recording_75_seconds_gives_bytes_3_to_5()
    {
        AppState state = State(RecordingState.Recording);
        state = state with { Obs = state.Obs with { Recorded = TimeSpan.FromSeconds(75.9) } };

        byte[] payload = ConsoleFeedRig.Payload(ConsoleProtocol.ScreenStateLine(Project(state).Screen));

        Assert.Equal(new byte[] { 75, 0, 0 }, payload[3..6]);
    }

    [Fact]
    public void The_paused_seconds_count_from_the_last_recording_change_against_now_only()
    {
        AppState paused = State(RecordingState.Paused);
        paused = paused with { Obs = paused.Obs with { RecordingChangedOn = Now - TimeSpan.FromSeconds(130) } };
        AppState recording = paused with { Obs = paused.Obs with { Recording = RecordingState.Recording } };

        Assert.Equal(130, Project(paused).Screen.PausedSec);
        Assert.Equal(0, ConsoleProjection.Project(paused, Sample, Now - TimeSpan.FromHours(1)).Screen.PausedSec);
        Assert.Equal(0, Project(recording).Screen.PausedSec);
    }

    [Fact]
    public void Ready_comes_after_a_clean_preflight_and_goes_away_when_recording_starts()
    {
        AppState stopped = State() with { Preflight = Report(Now - TimeSpan.FromMinutes(1), ConditionOutcome.Pass) };
        stopped = stopped with { Obs = stopped.Obs with { RecordingChangedOn = Now - TimeSpan.FromMinutes(10) } };
        AppState started = stopped with { Obs = stopped.Obs with { Recording = RecordingState.Recording, RecordingChangedOn = Now } };
        AppState stoppedAgain = started with { Obs = started.Obs with { Recording = RecordingState.Stopped } };

        Assert.Equal(ScreenFlags.PreflightRan, Project(stopped).Screen.Flags & Preflight);
        Assert.Equal(ScreenFlags.None, Project(started).Screen.Flags & Preflight);
        Assert.Equal(ScreenFlags.None, Project(stoppedAgain).Screen.Flags & Preflight);
        Assert.Equal(ScreenFlags.None, Project(State()).Screen.Flags & Preflight);
    }

    [Theory]
    [InlineData(ConditionOutcome.Fail)]
    [InlineData(ConditionOutcome.Skipped)]
    public void A_preflight_with_a_failure_or_something_unchecked_says_check(ConditionOutcome outcome)
    {
        AppState state = State() with { Preflight = Report(Now, ConditionOutcome.Pass, outcome) };

        Assert.Equal(Preflight, Project(state).Screen.Flags & Preflight);
    }

    [Fact]
    public void Mic_muted_sets_the_flag_and_the_small_line_and_otherwise_the_line_is_empty()
    {
        AppState muted = State() with { Obs = State().Obs with { MicMuted = true } };

        Assert.True(Project(muted).Screen.Flags.HasFlag(ScreenFlags.MicMuted));
        Assert.Equal(new ConsoleText("MIC MUDO"), Project(muted).SmallLine);
        Assert.Equal(new ConsoleText(""), Project(State()).SmallLine);
    }

    [Theory]
    [InlineData(ObsLinkState.Switching, "CAMBIANDO")]
    [InlineData(ObsLinkState.Closed, "NO ESTA ABIERTO")]
    [InlineData(ObsLinkState.AuthFailed, "CLAVE RECHAZADA")]
    public void Without_obs_the_small_line_says_why(ObsLinkState link, string text) =>
        Assert.Equal(new ConsoleText(text), Project(State(link: link)).SmallLine);

    [Theory]
    [InlineData(RecordingState.Recording, false, true)]
    [InlineData(RecordingState.Paused, false, true)]
    [InlineData(RecordingState.Stopped, true, true)]
    [InlineData(RecordingState.Stopped, false, false)]
    public void Keep_awake_while_recording_paused_or_streaming(RecordingState recording, bool streaming, bool awake)
    {
        AppState state = State(recording);
        state = state with { Obs = state.Obs with { Streaming = streaming } };

        Assert.Equal(awake, Project(state).Screen.Flags2.HasFlag(ScreenFlags2.KeepAwake));
        Assert.Equal(0, Project(state).Screen.Alert);
    }

    [Fact]
    public void The_scene_line_is_the_scene_on_air_in_capitals_up_to_25_letters()
    {
        AppState state = State();
        state = state with { Obs = state.Obs with { SceneName = "Vertical Pantalla + Banco con cámara" } };

        Assert.Equal(new ConsoleText("CAMARA"), Project(State()).SceneLine);
        Assert.Equal(new ConsoleText("VERTICAL PANTALLA + BANCO"), Project(state).SceneLine);
    }

    [Fact]
    public void A_failed_action_shows_in_amber_for_5_seconds_and_a_failed_marker_does_not()
    {
        var recent = new RecentFailure(Notifier.ActionFailed, "Ir al banco", 2, Now - TimeSpan.FromSeconds(4));
        AppState failed = State() with { RecentFailure = recent };

        Assert.Equal(new ConsoleText("FALLO P2: IR AL BANCO", 1), Project(failed).SceneLine);
        Assert.Equal(new ConsoleText("CAMARA"), ConsoleProjection.Project(failed, Sample, Now + TimeSpan.FromSeconds(1)).SceneLine);
        Assert.Equal(new ConsoleText("CAMARA"), Project(failed with { RecentFailure = recent with { NoticeId = Notifier.MarkerFailed } }).SceneLine);
        Assert.Equal(
            new ConsoleText("FALLO: UNA ACCION CON UN", 1),
            Project(failed with { RecentFailure = recent with { ActionName = "una acción con un nombre largo", Step = null } }).SceneLine);
    }

    [Theory]
    [InlineData(RecordingState.Recording, 255, 0, 0, LedEffect.Steady)]
    [InlineData(RecordingState.Paused, 245, 166, 35, LedEffect.Blink1Hz)]
    [InlineData(RecordingState.Stopped, 0, 0, 0, LedEffect.Steady)]
    [InlineData(RecordingState.Starting, 0, 0, 0, LedEffect.Steady)]
    public void The_strip_goes_with_the_recording(RecordingState recording, byte r, byte g, byte b, LedEffect effect)
    {
        IReadOnlyList<LedColor> strip = Project(State(recording)).Strip;

        Assert.Equal(8, strip.Count);
        Assert.All(strip, led => Assert.Equal(new LedColor(r, g, b, effect), led));
    }

    [Fact]
    public void Without_obs_the_strip_is_off_even_if_the_last_known_was_recording() =>
        Assert.All(Project(State(RecordingState.Recording, ObsLinkState.Lost)).Strip, led => Assert.Equal(LedColor.Off, led));

    private static PreflightReport Report(DateTimeOffset at, params ConditionOutcome[] outcomes) =>
        new(Guid.NewGuid(), at, Trigger.Hotkey, true, [.. outcomes.Select(outcome => new CheckResult("obs.micNotMuted", "m", outcome, ""))]);

    // Revisión de la tanda 2 de la 9a (should): al arrancar y al detener la tira va apagada y la consola no se queda despierta.
    [Theory]
    [InlineData(RecordingState.Starting)]
    [InlineData(RecordingState.Stopping)]
    public void While_starting_or_stopping_the_strip_is_off_and_the_console_may_sleep(RecordingState recording)
    {
        ConsoleFrame frame = Project(State(recording));

        Assert.All(frame.Strip, led => Assert.Equal(LedColor.Off, led));
        Assert.Equal(ScreenFlags2.None, frame.Screen.Flags2);
    }

    // Revisión de la tanda 2 de la 9a (should): sin OBS listo no se muestra nada viejo, ni segundos, ni micrófono, ni escena.
    [Fact]
    public void Without_obs_ready_the_stale_seconds_mic_and_scene_are_not_shown()
    {
        AppState lost = State(RecordingState.Recording, ObsLinkState.Lost);
        lost = lost with { Obs = lost.Obs with { Recorded = TimeSpan.FromSeconds(90), MicMuted = true, SceneName = "Cámara" } };

        ConsoleFrame frame = Project(lost);

        Assert.Equal(0, frame.Screen.RecordedSec);
        Assert.Equal(ScreenFlags.None, frame.Screen.Flags & ScreenFlags.MicMuted);
        Assert.Equal("", frame.SceneLine.Text);
    }
}
