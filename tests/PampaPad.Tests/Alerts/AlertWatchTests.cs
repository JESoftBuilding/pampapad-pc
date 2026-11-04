using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El servicio del vigía con reloj falso: escribe AppState.Alert, apaga la marca a los 2 s y lanza el chequeo al empezar (E9b-3).</summary>
public sealed class AlertWatchTests : IAsyncDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _clock = new(AlertRun.T0);
    private readonly StateStore _state = new();
    private readonly Log _log;
    private readonly List<GraveAlert> _appeared = [];
    private Func<CancellationToken, Task<PreflightReport>> _check;
    private int _checks;

    public AlertWatchTests()
    {
        _log = new Log(_temp.Combine("log"), _clock);
        _check = _ => Task.FromResult(Report());
    }

    public async ValueTask DisposeAsync()
    {
        await _state.DisposeAsync();
        await _log.DisposeAsync();
        _temp.Dispose();
    }

    [Fact]
    public async Task A_change_of_the_state_leaves_the_alert_in_the_state_and_it_appears_once()
    {
        using AlertWatch watch = Start();
        _state.Update(s => s with { Obs = AlertRun.Obs(RecordingState.Recording, micMuted: true).Obs });

        await Poll.Until(() => _state.Current.Alert?.Id == AlertTexts.MicMuted);
        _clock.Advance(TimeSpan.FromSeconds(3));
        await Poll.Until(() => watch.Memory.ReadyRecording == RecordingState.Recording);

        Assert.Single(_appeared);
    }

    [Fact]
    public async Task Marker_failed_goes_away_at_two_seconds_without_waiting_for_another_change()
    {
        using AlertWatch watch = Start();
        _state.Update(s => s with { RecentFailure = new RecentFailure(Notifier.MarkerFailed, "Marca", 1, _clock.GetUtcNow(), ErrorCodes.MarkerPaused) });
        await Poll.Until(() => _state.Current.Alert is not null);

        _clock.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.NotNull(_state.Current.Alert);
        _clock.Advance(TimeSpan.FromMilliseconds(1));

        await Poll.Until(() => _state.Current.Alert is null);
    }

    [Fact]
    public async Task The_click_attends_the_alert_on_view_and_without_one_does_nothing()
    {
        using AlertWatch watch = Start();
        watch.Attend();
        _state.Update(s => s with { Obs = AlertRun.Obs(RecordingState.Recording, micMuted: true).Obs });
        await Poll.Until(() => _state.Current.Alert is not null);

        watch.Attend();

        Assert.Null(_state.Current.Alert);
        Assert.Contains(_log.Recent(), entry => entry.Module == AlertWatch.Module && entry.Message == "Aviso mic_muted apagado: atendido con un clic.");
    }

    [Fact]
    public async Task Starting_a_take_runs_the_check_once_and_its_first_failure_becomes_the_alert_and_the_small_line()
    {
        _check = _ => Task.FromResult(Report(new CheckResult("obs.cameraNotCovered", "La cámara está tapada", ConditionOutcome.Fail, "negra")));
        using AlertWatch watch = Start();
        _state.Update(s => s with { Obs = AlertRun.Obs().Obs });
        _state.Update(s => s with { Obs = AlertRun.Obs(RecordingState.Recording).Obs });

        await Poll.Until(() => _state.Current.Alert?.Id == AlertTexts.RecordingWithFailure);
        _clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal("CAMARA TAPADA", _state.Current.Alert!.Line2);
        Assert.Equal("CAMARA TAPADA", _state.Current.TakeCheckFailure);
        Assert.Equal(1, Volatile.Read(ref _checks));
        Assert.Null(_state.Current.Preflight);
        Assert.Contains(_log.Recent(), entry => entry.Message.StartsWith("Chequeo al empezar a grabar: 1 comprobaciones y 1 con falla", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_check_that_takes_more_than_three_seconds_does_not_alert_and_leaves_a_line()
    {
        _check = async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(4), _clock, CancellationToken.None);
            return Report(new CheckResult("obs.cameraNotCovered", "La cámara está tapada", ConditionOutcome.Fail, "negra"));
        };
        using AlertWatch watch = Start();
        _state.Update(s => s with { Obs = AlertRun.Obs(RecordingState.Recording).Obs });
        await Poll.Until(() => Volatile.Read(ref _checks) == 1);

        // De a poco: el tope de WaitAsync se arma recién cuando el chequeo devuelve su tarea, y un salto de 3 s podría pasarle por encima.
        for (int step = 0; step < 50; step++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(1);
        }

        await Poll.Until(() => _log.Recent().Any(entry => entry.Message.Contains("pasó el tope de 3 s", StringComparison.Ordinal)));

        Assert.Null(_state.Current.Alert);
        Assert.Null(_state.Current.TakeCheckFailure);
    }

    private static PreflightReport Report(params CheckResult[] checks) => new(Guid.NewGuid(), AlertRun.T0, Trigger.Internal, true, checks);

    private AlertWatch Start()
    {
        var watch = new AlertWatch(
            _state,
            () => new Config(),
            ct =>
            {
                Interlocked.Increment(ref _checks);
                return _check(ct);
            },
            _log,
            _clock,
            alert =>
            {
                lock (_appeared)
                {
                    _appeared.Add(alert);
                }
            });
        watch.Start();
        return watch;
    }
}
