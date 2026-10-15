using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class UpdateCycleTests : IAsyncLifetime
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeUpdateFeed _feed = new();
    private readonly Log _log;
    private readonly StateStore _state;
    private readonly LocalSettings _settings;
    private RestartVerdict _verdict = new(true);

    public UpdateCycleTests()
    {
        _log = new Log(_temp.Combine("log"), _clock);
        _state = new StateStore(_log);
        _settings = new LocalSettings(_temp.Combine("local.json"), _log);
    }

    private UpdateState Update => _state.Current.Update;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _state.DisposeAsync();
        await _log.DisposeAsync();
        _temp.Dispose();
    }

    [Fact]
    public async Task First_check_comes_after_15_seconds_and_then_every_6_hours()
    {
        using var stop = new CancellationTokenSource();
        var loop = Cycle().KeepCheckingAsync(stop.Token);

        _clock.Advance(TimeSpan.FromSeconds(14));
        Assert.False(await _feed.WaitForFindAsync(TimeSpan.FromMilliseconds(250)));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await _feed.WaitForFindAsync());

        _clock.Advance(TimeSpan.FromHours(6) - TimeSpan.FromSeconds(1));
        Assert.False(await _feed.WaitForFindAsync(TimeSpan.FromMilliseconds(250)));
        Assert.Equal(1, _feed.Finds);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await _feed.WaitForFindAsync());
        Assert.Equal(2, _feed.Finds);

        stop.Cancel();
        await loop;
    }

    [Fact]
    public async Task A_version_found_by_the_periodic_check_is_offered_but_never_downloaded()
    {
        _feed.FindResult = "0.1.1";
        using var stop = new CancellationTokenSource();
        var loop = Cycle().KeepCheckingAsync(stop.Token);

        _clock.Advance(UpdateCycle.FirstCheckDelay);
        Assert.True(await _feed.WaitForFindAsync());
        stop.Cancel();
        await loop;

        Assert.Equal(new UpdateState(UpdatePhase.Available, "0.1.1"), Update);
        Assert.Equal(0, _feed.Downloads);
        Assert.Equal(0, _feed.Applies);
    }

    [Fact]
    public async Task A_check_that_fails_changes_nothing_and_leaves_a_line_in_the_log()
    {
        _feed.FindResult = new Failure(ErrorCodes.NoResponse, "Sin red");

        var result = await Cycle().CheckAsync(CancellationToken.None);

        Assert.Equal(ErrorCodes.NoResponse, result.Failure?.Code);
        Assert.Equal(UpdateState.Idle, Update);
        Assert.Contains(_log.Recent(), entry => entry.Level == LogLevel.Warning && entry.Code == ErrorCodes.NoResponse);
    }

    [Fact]
    public async Task A_check_that_fails_keeps_an_offer_that_was_already_there()
    {
        var cycle = await CycleOfferingAsync("0.1.1");
        _feed.FindResult = new Failure(ErrorCodes.NoResponse, "Sin red");

        await cycle.CheckAsync(CancellationToken.None);

        Assert.Equal(new UpdateState(UpdatePhase.Available, "0.1.1"), Update);
    }

    [Fact]
    public async Task A_skipped_version_is_not_offered_but_the_next_one_is()
    {
        await _settings.SaveAsync(data => data with { SkippedVersion = "0.1.1" }, CancellationToken.None);
        var cycle = Cycle();

        _feed.FindResult = "0.1.1";
        await cycle.CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateState.Idle, Update);

        _feed.FindResult = "0.1.2";
        await cycle.CheckAsync(CancellationToken.None);
        Assert.Equal(new UpdateState(UpdatePhase.Available, "0.1.2"), Update);
    }

    [Fact]
    public async Task Skip_saves_the_version_in_local_json_and_withdraws_the_offer()
    {
        var cycle = await CycleOfferingAsync("0.1.1");

        var result = await cycle.SkipAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("0.1.1", _settings.Current.SkippedVersion);
        Assert.Contains("\"skippedVersion\": \"0.1.1\"", File.ReadAllText(_temp.Combine("local.json")));
        Assert.Equal(UpdateState.Idle, Update);
    }

    [Fact]
    public async Task Install_downloads_and_leaves_the_version_to_be_applied_on_exit()
    {
        var cycle = await CycleOfferingAsync("0.1.1");

        var result = await cycle.DownloadAndApplyAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(1, _feed.Downloads);
        Assert.Equal(1, _feed.Applies);
        Assert.Equal(new UpdateState(UpdatePhase.Applying, "0.1.1"), Update);
    }

    [Fact]
    public async Task A_check_that_lands_while_the_install_is_pending_does_not_reopen_the_offer()
    {
        var cycle = await CycleOfferingAsync("0.1.1");
        await cycle.DownloadAndApplyAsync(CancellationToken.None);
        var findsBefore = _feed.Finds;

        var result = await cycle.CheckAsync(CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(findsBefore, _feed.Finds);
        Assert.Equal(new UpdateState(UpdatePhase.Applying, "0.1.1"), Update);
    }

    [Fact]
    public async Task Install_is_refused_while_the_policy_says_no()
    {
        var cycle = await CycleOfferingAsync("0.1.1");
        _verdict = new RestartVerdict(false, "Se está grabando");

        var result = await cycle.DownloadAndApplyAsync(CancellationToken.None);

        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, result.Failure?.Code);
        Assert.Equal("Se está grabando", result.Failure?.Message);
        Assert.Equal(0, _feed.Downloads);
        Assert.Equal(0, _feed.Applies);
        Assert.Equal(new UpdateState(UpdatePhase.Available, "0.1.1"), Update);    // el motivo no queda en el estado
        Assert.Contains(_log.Recent(), entry => entry.Module == "update" && entry.Message == "No se instala la versión 0.1.1: Se está grabando");
    }

    [Fact]
    public async Task A_download_is_not_applied_if_the_policy_changes_meanwhile()
    {
        _feed.FindResult = "0.1.1";
        var asked = 0;
        var cycle = new UpdateCycle(
            _state,
            _settings,
            _log,
            _clock,
            _feed.AsFeed(),
            _ => ++asked == 1 ? new RestartVerdict(true) : new RestartVerdict(false, "Empezó una toma"));
        await cycle.CheckAsync(CancellationToken.None);

        var result = await cycle.DownloadAndApplyAsync(CancellationToken.None);

        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, result.Failure?.Code);
        Assert.Equal(1, _feed.Downloads);
        Assert.Equal(0, _feed.Applies);
        Assert.Equal(new UpdateState(UpdatePhase.Available, "0.1.1"), Update);
        Assert.Contains(_log.Recent(), entry => entry.Module == "update" && entry.Message == "No se instala la versión 0.1.1: Empezó una toma");
    }

    [Fact]
    public async Task A_refused_install_leaves_no_reason_behind_once_the_take_ends()
    {
        // Revisión de la etapa 2: el motivo quedaba en Detail, la bandeja seguía diciendo "Se está grabando" con la toma
        // terminada (hasta 6 h) y una búsqueda que fallaba lo reponía. Con la política real, como la arma Updater.
        var connected = ObsState.Initial with { Link = ObsLinkState.Ready };
        var cycle = new UpdateCycle(_state, _settings, _log, _clock, _feed.AsFeed(), RestartPolicy.Evaluate);
        _feed.FindResult = "0.2.0";
        await cycle.CheckAsync(CancellationToken.None);
        _state.Update(current => current with { Obs = connected with { Recording = RecordingState.Recording } });

        var refused = await cycle.DownloadAndApplyAsync(CancellationToken.None);
        _state.Update(current => current with { Obs = connected });
        _feed.FindResult = new Failure(ErrorCodes.NoResponse, "Sin red");
        await cycle.CheckAsync(CancellationToken.None);

        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, refused.Failure?.Code);
        Assert.Equal(new UpdateState(UpdatePhase.Available, "0.2.0"), Update);
        Assert.Equal("Versión 0.2.0 lista para instalar", TraySummary.From(_state.Current).Tooltip);
        Assert.Equal(new UpdateMenu(true, "Buscar actualización", true, true, "Instalar versión 0.2.0"), TraySummary.UpdateItems(_state.Current));
    }

    [Fact]
    public async Task A_download_that_fails_goes_back_to_available_and_can_be_retried()
    {
        var cycle = await CycleOfferingAsync("0.1.1");
        _feed.DownloadResult = Result.Fail(ErrorCodes.NoResponse, "Se cortó");

        var failed = await cycle.DownloadAndApplyAsync(CancellationToken.None);

        Assert.Equal(ErrorCodes.NoResponse, failed.Failure?.Code);
        Assert.Equal(new UpdateState(UpdatePhase.Available, "0.1.1", "No se pudo bajar: Se cortó"), Update);
        Assert.Equal(0, _feed.Applies);

        _feed.DownloadResult = Result.Success;
        var retried = await cycle.DownloadAndApplyAsync(CancellationToken.None);

        Assert.True(retried.Ok);
        Assert.Equal(1, _feed.Applies);
    }

    [Fact]
    public async Task An_apply_that_fails_keeps_the_offer_with_the_reason()
    {
        var cycle = await CycleOfferingAsync("0.1.1");
        _feed.ApplyResult = Result.Fail(ErrorCodes.InternalError, "Update.exe no arrancó");

        var result = await cycle.DownloadAndApplyAsync(CancellationToken.None);

        Assert.Equal(ErrorCodes.InternalError, result.Failure?.Code);
        Assert.Equal(new UpdateState(UpdatePhase.Available, "0.1.1", "No se pudo instalar: Update.exe no arrancó"), Update);
        Assert.Contains(_log.Recent(), entry => entry.Level == LogLevel.Error && entry.Module == "update");
    }

    [Fact]
    public async Task Install_without_an_offer_fails_and_touches_nothing()
    {
        var result = await Cycle().DownloadAndApplyAsync(CancellationToken.None);

        Assert.Equal(ErrorCodes.InvalidData, result.Failure?.Code);
        Assert.Equal(0, _feed.Downloads);
        Assert.Equal(UpdateState.Idle, Update);
    }

    private UpdateCycle Cycle() => new(_state, _settings, _log, _clock, _feed.AsFeed(), _ => _verdict);

    private async Task<UpdateCycle> CycleOfferingAsync(string version)
    {
        var cycle = Cycle();
        _feed.FindResult = version;
        await cycle.CheckAsync(CancellationToken.None);
        return cycle;
    }
}
