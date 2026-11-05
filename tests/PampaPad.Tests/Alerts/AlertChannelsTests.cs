using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que un aviso grave hace en la PC y de dónde sale el clic: el sonido con la regla, el ícono y la perilla (plan de la 9b, E9b-3).</summary>
public class AlertChannelsTests
{
    private static readonly GraveAlert Mic = new(AlertTexts.MicMuted, 2, 2, "MIC MUDO", "ESTAS GRABANDO", "", "MIC: ABRIR   CLICK: YA SE", AlertRun.T0, true, true);

    // Grabando, el zumbador suena (Sound viaja en el aviso) y el sonido de la PC sigue la regla, que por defecto lo calla.
    [Theory]
    [InlineData(RecordingState.Recording, 0)]
    [InlineData(RecordingState.Stopped, 1)]
    public async Task The_pc_sound_follows_the_rule_once_when_an_alert_appears(RecordingState recording, int sounds)
    {
        await using var rig = new NotifierRig(recording: recording);

        rig.Notifier.OnAlertAppeared(Mic);

        Assert.Equal(sounds, rig.Sounds.Count);
        Assert.Empty(rig.Balloons);
    }

    [Fact]
    public async Task Marker_failed_does_not_sound_twice()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnAlertAppeared(Mic with { Id = AlertTexts.MarkerFailed, Number = 1 });

        Assert.Empty(rig.Sounds);
    }

    [Fact]
    public void The_tray_shows_the_failed_icon_with_the_name_and_obs_lost_the_no_obs_one()
    {
        AppState recording = AlertRun.Obs(RecordingState.Recording);

        TraySummary mic = TraySummary.From(recording with { Alert = Mic });
        TraySummary lost = TraySummary.From(AlertRun.Obs() with { Alert = Mic with { Id = AlertTexts.ObsLost, Number = 5 } });
        TraySummary marker = TraySummary.From(recording with { Alert = Mic with { Id = AlertTexts.MarkerFailed, Number = 1 } });

        Assert.Equal(TrayIcon.ActionFailed, mic.Icon);
        Assert.StartsWith("Aviso: micrófono silenciado grabando · Grabando", mic.Tooltip);
        Assert.Equal(TrayIcon.NoObs, lost.Icon);
        Assert.Equal(TrayIcon.Recording, marker.Icon);
    }

    [Fact]
    public async Task A_tap_of_the_knob_is_the_click_also_while_recording_and_the_press_alone_is_not()
    {
        await using var rig = new DispatcherRig("""{ "version": 1, "pages": [ { "id": "toma", "keys": [] } ] }""", clock: new FakeTimeProvider());
        rig.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });
        int clicks = 0;
        rig.Dispatcher.KnobTapped += () => clicks++;

        rig.Dispatcher.Receive(new InputEvent("console", Controls.Knob, Gesture.Press));
        Assert.Equal(0, clicks);

        // Gestures hace del apretar y soltar un toque.
        rig.Dispatcher.Receive(new InputEvent("console", Controls.Knob, Gesture.Release));
        Assert.Equal(1, clicks);
        rig.Dispatcher.Receive(new InputEvent("console", Controls.Knob, Gesture.Turn, 1));
        Assert.Equal(1, clicks);
    }
}
