using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

/// <summary>Las condiciones que miran la escena al aire, contra el OBS falso con los elementos medidos el 2026-10-13.</summary>
public class SceneConditionsTests
{
    [Fact]
    public async Task The_phone_mirror_closed_has_no_image_and_open_it_has()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await ConditionProbe.OnAirAsync(session, "Banco", FakeObs.BancoUuid);

        ConditionResult closed = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.sourceHasImage" }""");
        session.Obs.MirrorOpen = true;
        ConditionResult open = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.sourceHasImage" }""");

        Assert.Equal(ConditionOutcome.Fail, closed.Outcome);
        Assert.Equal("\"Celu (scrcpy)\" mide 0 x 0: no tiene imagen.", closed.Detail);
        Assert.Equal(ConditionOutcome.Pass, open.Outcome);
    }

    [Fact]
    public async Task A_source_named_that_is_not_on_air_does_not_apply()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        ConditionResult result = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.sourceHasImage", "source": "Celu (scrcpy)" }""");

        Assert.Equal(ConditionOutcome.Pass, result.Outcome);
        Assert.StartsWith("No aplica", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_scene_with_only_a_page_has_no_capture_to_look_at()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await ConditionProbe.OnAirAsync(session, "Intro", "49ecf855-9581-4dc8-a4d0-4bd0aef50c29");

        ConditionResult image = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.sourceHasImage" }""");
        ConditionResult monitor = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.captureHasMonitor" }""");

        Assert.StartsWith("No aplica", image.Detail, StringComparison.Ordinal);
        Assert.StartsWith("No aplica", monitor.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_capture_has_its_monitor_until_obs_stops_listing_it()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        ConditionResult listed = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.captureHasMonitor" }""");
        session.Obs.Monitors.Clear();
        ConditionResult gone = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.captureHasMonitor" }""");

        Assert.Equal(ConditionOutcome.Pass, listed.Outcome);
        Assert.Equal(ConditionOutcome.Fail, gone.Outcome);
        Assert.Equal("\"Captura de pantalla\" apunta a un monitor que OBS no lista: ¿está apagado o en otra PC?", gone.Detail);
    }

    [Fact]
    public async Task The_camera_with_its_cover_has_no_deviation()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        ConditionResult open = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.cameraNotCovered" }""");
        session.Obs.CameraCovered = true;
        ConditionResult covered = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.cameraNotCovered" }""");

        Assert.Equal(ConditionOutcome.Pass, open.Outcome);
        Assert.Equal(ConditionOutcome.Fail, covered.Outcome);
        Assert.Equal("La cámara \"Dispositivo de captura de video\" se ve tapada o en negro: desvío 0,0; se pide 5,0 o más.", covered.Detail);
    }

    [Fact]
    public async Task A_camera_that_obs_says_is_not_active_does_not_apply()
    {
        await using ObsSession session = await ObsSession.StartAsync(before: obs => obs.CameraInactive = true);

        ConditionResult result = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.cameraNotCovered" }""");

        Assert.Equal(ConditionOutcome.Pass, result.Outcome);
        Assert.StartsWith("No aplica", result.Detail, StringComparison.Ordinal);
        Assert.Equal(0, session.Obs.Times("GetSourceScreenshot"));
    }

    [Fact]
    public async Task With_a_take_in_progress_no_screenshot_is_asked()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording, obs => obs.CameraCovered = true);

        ConditionResult result = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.cameraNotCovered" }""");

        Assert.Equal(ConditionOutcome.Pass, result.Outcome);
        Assert.StartsWith("No aplica", result.Detail, StringComparison.Ordinal);
        Assert.Equal(0, session.Obs.Times("GetSourceScreenshot"));
    }

    [Fact]
    public async Task Pantalla_fills_the_canvas_and_vertical_solidworks_does_not()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        ConditionResult pantalla = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.canvasFilled" }""");
        await ConditionProbe.OnAirAsync(session, "Vertical SolidWorks", FakeObs.VerticalSolidWorksUuid);
        ConditionResult vertical = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.canvasFilled" }""");

        Assert.Equal(ConditionOutcome.Pass, pantalla.Outcome);
        Assert.Equal(ConditionOutcome.Fail, vertical.Outcome);
        Assert.Equal("\"Vertical SolidWorks\" llena el 31,8 % del lienzo de 1920 x 1080; se pide 90 %.", vertical.Detail);
    }

    [Theory]
    [InlineData("""{ "type": "obs.sourceHasImage" }""")]
    [InlineData("""{ "type": "obs.captureHasMonitor" }""")]
    [InlineData("""{ "type": "obs.cameraNotCovered" }""")]
    [InlineData("""{ "type": "obs.canvasFilled" }""")]
    public async Task Without_obs_each_one_is_skipped_and_never_counts_as_fine(string json)
    {
        await using var offline = new OfflineObs();

        ConditionResult result = await ConditionProbe.EvaluateAsync(offline.Client, offline.State, json);

        Assert.Equal(ConditionOutcome.Skipped, result.Outcome);
        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
    }
}
