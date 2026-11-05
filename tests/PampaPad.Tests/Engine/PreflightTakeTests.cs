using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>El chequeo al empezar a grabar (plan de la 9b, decisión 7): sin la captura sin monitor ni el micrófono, y sin pisar AppState.Preflight.</summary>
public sealed class PreflightTakeTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task It_leaves_out_the_monitor_and_the_mic_and_does_not_touch_the_preflight_of_the_state()
    {
        FakeCondition camera = FakeCondition.NotMet("obs.cameraNotCovered");
        FakeCondition mic = FakeCondition.NotMet("obs.micNotMuted");
        FakeCondition monitor = FakeCondition.NotMet("obs.captureHasMonitor");
        Directory.CreateDirectory(_temp.Combine("biblioteca"));
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), """
            { "version": 1, "preflight": [
              { "condition": { "type": "obs.captureHasMonitor" }, "message": "Sin monitor" },
              { "condition": { "type": "obs.micNotMuted" }, "message": "Mic" },
              { "condition": { "type": "obs.cameraNotCovered" }, "message": "Cámara tapada" }
            ] }
            """);
        var library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        library.Load();
        await using var state = new StateStore();
        await using var log = new Log(_temp.Combine("log"), TimeProvider.System);
        var preflight = new Preflight([camera.Type, mic.Type, monitor.Type], library, state, log, TimeProvider.System);

        PreflightReport report = await preflight.CheckTakeAsync(CancellationToken.None);

        Assert.Equal(new[] { "obs.cameraNotCovered" }, report.Checks.Select(check => check.Type));
        Assert.Equal((0, 0), (mic.Evaluations, monitor.Evaluations));
        Assert.Equal(Trigger.Internal, report.RequestedBy);
        Assert.Null(state.Current.Preflight);
    }

    // 03 §7: la captura de la cámara se pide "sólo detenido o al arrancar". El chequeo al empezar corre ya con la toma en curso y la pide igual;
    // una comprobación suelta, grabando, sigue sin pedirla. Lo encontró el repaso de la 9b (E9b-7).
    [Fact]
    public async Task At_take_start_the_covered_camera_is_checked_although_the_take_is_in_progress()
    {
        Directory.CreateDirectory(_temp.Combine("biblioteca"));
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), """
            { "version": 1, "preflight": [ { "condition": { "type": "obs.cameraNotCovered" }, "message": "La cámara está tapada" } ] }
            """);
        var library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        library.Load();
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording, obs => obs.CameraCovered = true);
        await using var log = new Log(_temp.Combine("log"), TimeProvider.System);
        var preflight = new Preflight(Conditions.Build(new FakeWindows(), session.State, session.Client), library, session.State, log, TimeProvider.System);

        PreflightReport report = await preflight.CheckTakeAsync(CancellationToken.None);
        ConditionResult loose = await ConditionProbe.EvaluateAsync(session, """{ "type": "obs.cameraNotCovered" }""");

        Assert.Equal(ConditionOutcome.Fail, Assert.Single(report.Checks).Outcome);
        Assert.Equal(1, session.Obs.Times("GetSourceScreenshot"));
        Assert.StartsWith("No aplica", loose.Detail, StringComparison.Ordinal);
    }
}
