using PampaPad.Core;
using PampaPad.Tests.Doubles;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public sealed class PreflightTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    // Una biblioteca con ese config.json y el chequeo armado como Composition, contra ese OBS.
    private Preflight Build(ObsClient obs, StateStore state, Log log, string config = """{ "version": 1 }""")
    {
        Directory.CreateDirectory(_temp.Combine("biblioteca"));
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), config);
        var library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        library.Load();
        return new Preflight(Conditions.Build(new FakeWindows(), state, obs), library, state, log, TimeProvider.System);
    }

    [Fact]
    public async Task Without_a_list_the_seven_of_the_program_run_in_order_and_each_leaves_a_line()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        Preflight preflight = Build(session.Client, session.State, session.Log);

        PreflightReport report = await preflight.RunAsync(Trigger.Tray, CancellationToken.None);

        Assert.True(report.Defaults);
        Assert.Equal(
            new[] { "obs.captureHasMonitor", "obs.sourceHasImage", "obs.cameraAudioOff", "obs.cameraNotCovered", "obs.recordFormatIs", "obs.micNotMuted", "obs.canvasFilled" },
            report.Checks.Select(check => check.Type));
        Assert.All(report.Checks, check => Assert.Equal(ConditionOutcome.Pass, check.Outcome));
        Assert.Equal(7, session.Log.Recent().Count(entry => entry.Module == "preflight" && entry.RunId == report.RunId && entry.Message.Contains(": bien.", StringComparison.Ordinal)));
        Assert.Same(report, session.State.Current.Preflight);
    }

    [Fact]
    public async Task It_does_not_stop_at_the_first_failure_and_says_each_one()
    {
        await using ObsSession session = await ObsSession.StartAsync(before: obs =>
        {
            obs.Monitors.Clear();
            obs.CameraCovered = true;
        });
        await ConditionProbe.OnAirAsync(session, "Vertical SolidWorks", FakeObs.VerticalSolidWorksUuid);
        Preflight preflight = Build(session.Client, session.State, session.Log);

        PreflightReport report = await preflight.RunAsync(Trigger.Tray, CancellationToken.None);

        Assert.Equal(new[] { "obs.captureHasMonitor", "obs.cameraNotCovered", "obs.canvasFilled" }, report.Failures.Select(check => check.Type));
        Assert.Equal("La escena no llena el lienzo", report.Failures[2].Message);
        Assert.Contains(session.Log.Recent(), entry => entry.Level == LogLevel.Warning
            && entry.Message == "obs.canvasFilled: falla. La escena no llena el lienzo: \"Vertical SolidWorks\" llena el 31,8 % del lienzo de 1920 x 1080; se pide 90 %.");
    }

    [Fact]
    public async Task With_a_list_exactly_that_list_runs_with_its_messages()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        Preflight preflight = Build(session.Client, session.State, session.Log, """
            { "version": 1, "preflight": [ { "condition": { "type": "obs.profileIs", "profile": "Reels vertical" }, "message": "OBS está en otro perfil" } ] }
            """);

        PreflightReport report = await preflight.RunAsync(Trigger.Tray, CancellationToken.None);

        Assert.False(report.Defaults);
        CheckResult only = Assert.Single(report.Checks);
        Assert.Equal(ConditionOutcome.Fail, only.Outcome);
        Assert.Equal("OBS está en otro perfil", only.Message);
    }

    [Fact]
    public async Task An_empty_list_runs_nothing()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        Preflight preflight = Build(session.Client, session.State, session.Log, """{ "version": 1, "preflight": [] }""");

        PreflightReport report = await preflight.RunAsync(Trigger.Tray, CancellationToken.None);

        Assert.False(report.Defaults);
        Assert.Empty(report.Checks);
    }

    [Fact]
    public async Task Without_obs_every_check_is_unchecked_and_none_fails()
    {
        await using var offline = new OfflineObs();
        Preflight preflight = Build(offline.Client, offline.State, offline.Log);

        PreflightReport report = await preflight.RunAsync(Trigger.Tray, CancellationToken.None);

        Assert.Equal(7, report.Unchecked.Count);
        Assert.Empty(report.Failures);
        Assert.All(report.Unchecked, check => Assert.Equal(ErrorCodes.ObsDisconnected, check.Code));
        Assert.Contains(offline.Log.Recent(), entry => entry.Message.StartsWith("obs.canvasFilled: sin comprobar. Sin OBS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unknown_or_broken_condition_is_unchecked_and_the_rest_still_runs()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        Preflight preflight = Build(session.Client, session.State, session.Log, """
            {
              "version": 1,
              "preflight": [
                { "condition": { "type": "obs.inventada" }, "message": "Inventada" },
                { "condition": { "type": "obs.profileIs" }, "message": "Sin perfil" },
                { "condition": { "type": "obs.profileIs", "profile": "Sin Título" }, "message": "OBS está en otro perfil" }
              ]
            }
            """);

        PreflightReport report = await preflight.RunAsync(Trigger.Tray, CancellationToken.None);

        Assert.Equal(new[] { ConditionOutcome.Skipped, ConditionOutcome.Skipped, ConditionOutcome.Pass }, report.Checks.Select(check => check.Outcome));
        Assert.All(report.Unchecked, check => Assert.Equal(ErrorCodes.ConfigInvalid, check.Code));
    }

    [Fact]
    public async Task An_unknown_or_broken_condition_of_the_list_is_also_a_problem_of_the_load()
    {
        await using var offline = new OfflineObs();

        AppState loaded = Load(offline, """
            {
              "version": 1,
              "preflight": [
                { "condition": { "type": "obs.inventada" }, "message": "Inventada" },
                { "condition": { "type": "obs.profileIs" }, "message": "Sin perfil" },
                { "condition": { "type": "obs.profileIs", "profile": "Sin Título" }, "message": "OBS está en otro perfil" }
              ]
            }
            """);

        // 02 §10, problemas 7 y 8: la tercera está bien y no da problema.
        Assert.Equal(
            new[] { ("preflight[0].condition.type", ProblemCodes.ConditionUnknown), ("preflight[1].condition.profile", ProblemCodes.ParameterInvalid) },
            loaded.Problems.Select(problem => (problem.Where, problem.Code)));
        Assert.All(loaded.Problems, problem => Assert.Equal(Severity.Warning, problem.Severity));
    }

    // La biblioteca con ese config.json, cargada por un Dispatcher con el catálogo real de condiciones, como en Composition: así se ven los
    // problemas de la carga, los que muestra el ícono de la bandeja.
    private AppState Load(OfflineObs offline, string config)
    {
        Directory.CreateDirectory(_temp.Combine("biblioteca"));
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), config);
        var library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        library.Load();
        var executor = new Executor([], Conditions.Build(new FakeWindows(), offline.State, offline.Client), TimeProvider.System);
        _ = new Dispatcher(library, offline.State, executor, new Gestures(TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness), offline.Log);
        return offline.State.Current;
    }

    [Fact]
    public async Task Cancelling_leaves_what_is_left_unchecked()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        Preflight preflight = Build(session.Client, session.State, session.Log);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        PreflightReport report = await preflight.RunAsync(Trigger.Tray, cancel.Token);

        Assert.Equal(7, report.Unchecked.Count);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void The_line_of_each_check_says_how_it_went_and_what_was_measured()
    {
        Assert.Equal("obs.micNotMuted: bien. El micrófono \"Mic/Aux\" está abierto.", Preflight.Line(new CheckResult("obs.micNotMuted", "El micrófono está silenciado", ConditionOutcome.Pass, "El micrófono \"Mic/Aux\" está abierto.")));
        Assert.Equal("obs.micNotMuted: falla. El micrófono está silenciado: El micrófono \"Mic/Aux\" está silenciado.", Preflight.Line(new CheckResult("obs.micNotMuted", "El micrófono está silenciado", ConditionOutcome.Fail, "El micrófono \"Mic/Aux\" está silenciado.")));
        Assert.Equal("obs.micNotMuted: sin comprobar. Sin OBS: no se pudo leer el micrófono.", Preflight.Line(new CheckResult("obs.micNotMuted", "El micrófono está silenciado", ConditionOutcome.Skipped, "Sin OBS: no se pudo leer el micrófono.", ErrorCodes.ObsDisconnected)));
    }
}
