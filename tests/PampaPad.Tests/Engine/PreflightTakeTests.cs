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
}
