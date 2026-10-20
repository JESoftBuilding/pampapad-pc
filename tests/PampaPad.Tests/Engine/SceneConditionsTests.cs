using System.Text.Json;
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
    public async Task A_source_named_that_is_not_on_air_or_not_of_the_kind_it_looks_at_does_not_apply()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        ConditionResult away = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.sourceHasImage", "source": "Celu (scrcpy)" }""");
        ConditionResult screen = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.cameraNotCovered", "source": "Captura de pantalla" }""");
        await ConditionProbe.OnAirAsync(session, "Pantalla + Cámara", "5bcf806a-9dac-430c-a437-3eab544e8419");
        ConditionResult mic = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.sourceHasImage", "source": "Mic/Aux" }""");
        await ConditionProbe.OnAirAsync(session, "Banco", FakeObs.BancoUuid);
        ConditionResult phone = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.captureHasMonitor", "source": "Celu (scrcpy)" }""");

        // Mic/Aux se ve en Pantalla + Cámara y mide 0 x 0, como toda entrada de sólo audio; el celular no tiene monitor_id (OBS da 600).
        Assert.Equal("No aplica: \"Celu (scrcpy)\" no es una captura ni una cámara al aire en \"Pantalla\".", away.Detail);
        Assert.Equal("No aplica: \"Captura de pantalla\" no es una cámara al aire en \"Pantalla\".", screen.Detail);
        Assert.Equal(ConditionOutcome.Pass, mic.Outcome);
        Assert.Equal("No aplica: \"Mic/Aux\" no es una captura ni una cámara al aire en \"Pantalla + Cámara\".", mic.Detail);
        Assert.Equal(ConditionOutcome.Pass, phone.Outcome);
        Assert.Equal("No aplica: \"Celu (scrcpy)\" no es una captura de pantalla al aire en \"Banco\".", phone.Detail);
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

    // Medido en el repaso de E5-22 (2026-10-19): después de pedir la lista de monitor_id, la lectura siguiente da la captura en 0 x 0 (a los
    // 3 a 6 ms) y vuelve a 1920 x 1080 antes de 150 ms. El OBS falso lo imita en una sola lectura.
    [Fact]
    public async Task Asking_for_the_monitors_leaves_the_capture_without_image_for_the_next_read()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.captureHasMonitor" }""");
        ConditionResult next = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.sourceHasImage" }""");
        ConditionResult after = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.sourceHasImage" }""");

        Assert.Equal("\"Captura de pantalla\" mide 0 x 0: no tiene imagen.", next.Detail);
        Assert.Equal(ConditionOutcome.Pass, after.Outcome);
    }

    // Ese reinicio, grabando, deja un cuadro negro en la toma (repaso de E5-22): con una toma en curso no se pregunta (decisión 34).
    [Theory]
    [InlineData(RecordingState.Recording)]
    [InlineData(RecordingState.Paused)]
    public async Task With_a_take_in_progress_the_monitor_is_not_asked(RecordingState recording)
    {
        await using ObsSession session = await ObsSession.StartAsync(recording, obs => obs.Monitors.Clear());

        ConditionResult result = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.captureHasMonitor" }""");

        Assert.Equal(ConditionOutcome.Pass, result.Outcome);
        Assert.Equal("No aplica: con una toma en curso no se pregunta por el monitor, que haría parpadear la captura.", result.Detail);
        Assert.Equal(0, session.Obs.Times("GetInputPropertiesListPropertyItems"));
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

    // El token llega a OBS: el chequeo corta con su tope cancelando (E5-17), y sin esto cada pedido esperaría su propio tope.
    [Theory]
    [InlineData("""{ "type": "obs.sourceHasImage" }""")]
    [InlineData("""{ "type": "obs.captureHasMonitor" }""")]
    [InlineData("""{ "type": "obs.cameraNotCovered" }""")]
    [InlineData("""{ "type": "obs.canvasFilled" }""")]
    public async Task With_the_token_already_cancelled_each_one_is_skipped_as_cancelled(string json)
    {
        await using ObsSession session = await ObsSession.StartAsync();

        ConditionResult result = await ConditionProbe.EvaluateAsync(session, json, new CancellationToken(true));

        Assert.Equal(ConditionOutcome.Skipped, result.Outcome);
        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
    }

    [Theory]
    [InlineData("""{ "type": "obs.sourceHasImage", "source": 5 }""")]
    [InlineData("""{ "type": "obs.captureHasMonitor", "source": "" }""")]
    [InlineData("""{ "type": "obs.cameraNotCovered", "source": { "uuid": "668dffa0-e0b7-4ae5-b469-a99fd220fb96" } }""")]
    public async Task A_source_that_is_not_a_reference_is_a_problem_of_the_condition(string json)
    {
        await using var offline = new OfflineObs();
        Condition condition = JsonSerializer.Deserialize<Condition>(json, Json.Options)!;
        ConditionType type = Conditions.Build(new FakeWindows(), offline.State, offline.Client).Single(candidate => candidate.Id == condition.Type);

        Problem problem = Assert.Single(ConditionRules.Validate(type, condition));

        Assert.Equal("source", problem.Where);
    }
}
