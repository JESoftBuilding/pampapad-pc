using PampaPad.Core;
using static PampaPad.Tests.ProjectionCase;

namespace PampaPad.Tests;

/// <summary>El aviso grave y el renglón chico en lo que sale por el cable (plan de la 9b, E9b-4; vectores de tools/consola.py).</summary>
public class ConsoleProjectionAlertTests
{
    private static readonly GraveAlert Mic = new(AlertTexts.MicMuted, 2, 2, "MIC MUDO", "ESTAS GRABANDO", "", "MIC: ABRIR   CLICK: YA SE", Now, Light: true, Sound: true);

    private static AppState Recording(bool micMuted = false) => Obs(State(RecordingState.Recording), obs => obs with { MicMuted = micMuted });

    private static AppState Obs(AppState state, Func<ObsState, ObsState> change) => state with { Obs = change(state.Obs) };

    [Fact]
    public void The_firmware_vectors_come_out_byte_by_byte()
    {
        var screen = new ScreenFrame(0, ConsoleRecording.Recording, ScreenFlags.PreflightRan, RecordedSec: 2052, ActiveMask: 1, Alert: 2, Flags2: ScreenFlags2.KeepAwake | ScreenFlags2.AlertSound);

        Assert.Equal("#401011AAMgBAgAAAAAZAABAAAAAgEFAAA=", ConsoleProtocol.ScreenStateLine(screen));
        Assert.Equal("#421011AgICCE1JQyBNVURP", ConsoleProtocol.ScreenTextLine(2, 2, 2, "MIC MUDO"));
        Assert.Equal("#421011BQACGU1JQzogQUJSSVIgICBDTElDSzogWUEgU0U=", ConsoleProtocol.ScreenTextLine(5, 0, 2, "MIC: ABRIR   CLICK: YA SE"));
        Assert.Equal("#421011AwAEDEhBQ0UgMzowMDowMA==", ConsoleProtocol.ScreenTextLine(3, 0, 4, "HACE 3:00:00"));
        Assert.Equal("#461011AAj/AAAC/wAAAv8AAAL/AAAC/wAAAv8AAAL/AAAC/wAAAg==", ConsoleProtocol.StripLine(Project(Recording(true) with { Alert = Mic }).Strip));
    }

    [Fact]
    public void The_alert_puts_its_number_in_byte_15_its_sound_bit_and_its_four_texts_with_the_title_tone()
    {
        ConsoleFrame frame = Project(Recording(true) with { Alert = Mic });

        Assert.Equal(2, frame.Screen.Alert);
        Assert.Equal(ScreenFlags2.KeepAwake | ScreenFlags2.AlertSound, frame.Screen.Flags2);
        Assert.Equal(new ConsoleAlert(2, new("MIC MUDO", 2), new("ESTAS GRABANDO"), new(""), new("MIC: ABRIR   CLICK: YA SE")), frame.Alert);
    }

    [Fact]
    public void With_light_and_sound_off_the_strip_stays_the_one_of_the_take_and_the_bit_goes_off()
    {
        ConsoleFrame frame = Project(Recording(true) with { Alert = Mic with { Light = false, Sound = false } });

        Assert.Equal(ScreenFlags2.KeepAwake, frame.Screen.Flags2);
        Assert.All(frame.Strip, led => Assert.Equal(new LedColor(255, 0, 0), led));
    }

    [Fact]
    public void An_amber_alert_blinks_amber_and_without_alert_byte_15_is_zero_and_the_strip_is_the_take()
    {
        ConsoleFrame amber = Project(State(RecordingState.Paused) with { Alert = Mic with { Id = AlertTexts.LongPause, Number = 3, Tone = 1 } });
        ConsoleFrame none = Project(State(RecordingState.Paused));

        Assert.All(amber.Strip, led => Assert.Equal(new LedColor(245, 166, 35, LedEffect.Blink4Hz), led));
        Assert.Equal(0, none.Screen.Alert);
        Assert.Null(none.Alert);
        Assert.All(none.Strip, led => Assert.Equal(new LedColor(245, 166, 35, LedEffect.Blink1Hz), led));
    }

    [Fact]
    public void Marker_failed_does_not_go_to_the_scene_line()
    {
        AppState state = Recording() with
        {
            RecentFailure = new RecentFailure(Notifier.MarkerFailed, "Marca", 1, Now, ErrorCodes.MarkerPaused),
            Alert = Mic with { Id = AlertTexts.MarkerFailed, Number = 1, Tone = 1, Title = "NO ENTRO" },
        };

        ConsoleFrame frame = Project(state);

        Assert.Equal(new ConsoleText("CAMARA"), frame.SceneLine);
        Assert.Equal(1, frame.Screen.Alert);
    }

    [Fact]
    public void The_small_line_is_mic_muted_then_the_failure_of_the_take_then_the_markers()
    {
        AppState take = Recording() with { TakeCheckFailure = "CAMARA TAPADA" };
        AppState marks = Obs(State(RecordingState.Paused), obs => obs with { TakeMarks = 7 });

        Assert.Equal("MIC MUDO", Project(take with { Obs = take.Obs with { MicMuted = true, TakeMarks = 7 } }).SmallLine.Text);
        Assert.Equal("CAMARA TAPADA", Project(take with { Obs = take.Obs with { TakeMarks = 7 } }).SmallLine.Text);
        Assert.Equal("7 MARCAS", Project(marks).SmallLine.Text);
        Assert.Equal("1 MARCA", Project(marks with { Obs = marks.Obs with { TakeMarks = 1 } }).SmallLine.Text);
        Assert.Equal("", Project(Recording()).SmallLine.Text);
    }

    [Fact]
    public void Stopped_the_small_line_is_the_first_failure_of_the_last_check_while_the_band_says_revisar()
    {
        var check = new PreflightReport(Guid.NewGuid(), Now, Trigger.Tray, true,
            [new CheckResult("obs.sourceHasImage", "x", ConditionOutcome.Pass, ""), new CheckResult("obs.canvasFilled", "x", ConditionOutcome.Fail, "")]);
        AppState stopped = State() with { Preflight = check, TakeCheckFailure = "CAMARA TAPADA" };
        stopped = stopped with { Obs = stopped.Obs with { TakeMarks = 3 } };

        Assert.Equal("ESCENA ANGOSTA", Project(stopped).SmallLine.Text);
        Assert.Equal("", Project(stopped with { Preflight = check with { Checks = [check.Checks[0]] } }).SmallLine.Text);
    }
}
