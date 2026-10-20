using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// La guarda del hallazgo E4-11, con el mismo reloj falso en el OBS falso, en el cliente y en el paso: nada se manda antes de un segundo
/// del último cambio que avisó OBS, y un grabar que OBS acepta y no anuncia es una falla.
/// </summary>
public class ObsRecordGuardTests
{
    private static readonly Invocation Chain = new(Guid.NewGuid(), Trigger.Key, "console", "key.3", Gesture.Press, 0, "grabar-y-parar");

    private static Step Do(string command) => JsonSerializer.Deserialize<Step>($$"""{ "type": "obs.record", "do": "{{command}}" }""", Json.Options)!;

    // El mismo reloj falso en el OBS falso y en el cliente, que es quien anota la hora de cada cambio de la grabación.
    private static Task<ObsSession> StartAsync(RecordingState recording, FakeTimeProvider clock) =>
        ObsSession.StartAsync(recording, obs => obs.Clock = clock, clock);

    private static ObsRecord StepOf(ObsSession session, TimeProvider clock) => new(session.Client, session.State, session.Log, clock);

    [Fact]
    public async Task Stopping_right_after_obs_said_it_records_waits_a_second_before_sending()
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Stopped, clock);
        await session.Obs.StartRecordingAsync();
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording);

        Task<StepResult> running = StepOf(session, clock).RunAsync(Do("stop"), Chain, CancellationToken.None);
        await Poll.Until(() => session.Log.Recent().Any(entry => entry.Message.Contains("se espera 1000 ms antes de mandar (E4-11)", StringComparison.Ordinal)));
        await Eventually.StepsAsync(clock, ObsRecord.Settle - TimeSpan.FromMilliseconds(1), 1);
        int sentEarly = session.Obs.Times("StopRecord");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, sentEarly);
        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times("StopRecord"));
    }

    [Fact]
    public async Task It_waits_only_what_is_left_of_the_second()
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Stopped, clock);
        await session.Obs.StartRecordingAsync();
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording);
        clock.Advance(TimeSpan.FromMilliseconds(400));

        Task<StepResult> running = StepOf(session, clock).RunAsync(Do("stop"), Chain, CancellationToken.None);
        await Poll.Until(() => session.Log.Recent().Any(entry => entry.Message.Contains("se espera 600 ms antes de mandar (E4-11)", StringComparison.Ordinal)));
        clock.Advance(TimeSpan.FromMilliseconds(600));
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times("StopRecord"));
    }

    [Fact]
    public async Task A_change_a_second_old_does_not_make_it_wait()
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Stopped, clock);
        await session.Obs.StartRecordingAsync();
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording);
        clock.Advance(ObsRecord.Settle);

        StepResult result = await StepOf(session, clock).RunAsync(Do("stop"), Chain, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times("StopRecord"));
        Assert.DoesNotContain(session.Log.Recent(), entry => entry.Message.Contains("(E4-11)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pausing_right_after_resuming_waits_too()
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Paused, clock);
        await session.Obs.ResumeRecordingAsync();
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording);

        Task<StepResult> running = StepOf(session, clock).RunAsync(Do("pause"), Chain, CancellationToken.None);
        await Poll.Until(() => session.Log.Recent().Any(entry => entry.Message.Contains("se espera 1000 ms antes de mandar (E4-11)", StringComparison.Ordinal)));
        int sentEarly = session.Obs.Times("PauseRecord");
        clock.Advance(ObsRecord.Settle);
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, sentEarly);
        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times("PauseRecord"));
    }

    [Fact]
    public async Task Recording_right_after_stopping_waits_a_second_and_then_confirms_the_start()
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Recording, clock);
        await session.Obs.StopRecordingAsync();
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Stopped);

        Task<StepResult> running = StepOf(session, clock).RunAsync(Do("start"), Chain, CancellationToken.None);
        await Poll.Until(() => session.Log.Recent().Any(entry => entry.Message.Contains("se espera 1000 ms antes de mandar (E4-11)", StringComparison.Ordinal)));
        int sentEarly = session.Obs.Times("StartRecord");
        clock.Advance(ObsRecord.Settle);
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, sentEarly);
        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times("StartRecord"));
        Assert.NotEqual(RecordingState.Stopped, session.State.Current.Obs.Recording);
    }

    [Fact]
    public async Task A_start_that_obs_accepts_and_never_announces_is_obs_timeout_and_the_chain_stops()
    {
        // El cliente va con el reloj real: la espera del aviso empieza después de la respuesta, que viaja por el socket, y el reloj falso
        // del paso se avanza hasta que termina sin vencer el pedido.
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Stopped, obs => obs.IgnoreNextStart = true);
        var clock = new FakeTimeProvider();

        Task<StepResult> running = StepOf(session, clock).RunAsync(Do("start"), Chain, CancellationToken.None);
        await Eventually.AdvancingAsync(clock, TimeSpan.FromMilliseconds(500), () => running.IsCompleted, "el paso termina");
        StepResult result = await running;

        Assert.Equal(1, session.Obs.Times("StartRecord"));
        Assert.Equal(ErrorCodes.ObsTimeout, result.Failure?.Code);
        Assert.Equal("OBS aceptó el pedido de grabar pero no avisó que arrancaba en 2 s: puede que no esté grabando.", result.Failure?.Message);
        Assert.Equal(RecordingState.Stopped, session.State.Current.Obs.Recording);
    }

    [Fact]
    public async Task What_obs_was_doing_when_the_program_connected_does_not_make_it_wait()
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Recording, clock);

        StepResult result = await StepOf(session, clock).RunAsync(Do("stop"), Chain, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times("StopRecord"));
    }

    [Fact]
    public async Task A_mark_ahead_of_the_clock_makes_it_wait_one_second_once_and_no_more()
    {
        // Una marca de otro reloj, o una que quedó adelante, daría una espera sin tope: se espera Settle una sola vez y se manda.
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Recording, clock);
        session.State.Update(s => s with { Obs = s.Obs with { RecordingChangedAt = clock.GetTimestamp() + TimeSpan.FromHours(1).Ticks } });

        Task<StepResult> running = StepOf(session, clock).RunAsync(Do("stop"), Chain, CancellationToken.None);
        await Poll.Until(() => session.Log.Recent().Any(entry => entry.Message.Contains("(E4-11)", StringComparison.Ordinal)));
        await Eventually.StepsAsync(clock, ObsRecord.Settle - TimeSpan.FromMilliseconds(1), 1);
        int sentEarly = session.Obs.Times("StopRecord");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, sentEarly);
        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times("StopRecord"));
        Assert.Single(session.Log.Recent(), entry => entry.Message.Contains("se espera 1000 ms antes de mandar (E4-11)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task If_the_link_drops_while_it_waits_the_transition_it_is_obs_disconnected_at_once()
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Stopped, clock);
        session.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Starting } });

        Task<StepResult> running = StepOf(session, clock).RunAsync(Do("stop"), Chain, CancellationToken.None);
        session.State.Update(s => s with { Obs = s.Obs.WithLink(ObsLinkState.Lost) });
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
        Assert.Equal(0, session.Obs.Times("StopRecord"));
    }

    // Con closed el estado pasa a stopped (ObsState.WithLink): sin mirar el enlace después del segundo, parar terminaría bien como "ya
    // está detenido" (03 §3: "Si el enlace se corta mientras espera, la falla es obs_disconnected").
    [Theory]
    [InlineData(ObsLinkState.Lost)]
    [InlineData(ObsLinkState.Closed)]
    public async Task If_the_link_drops_while_it_waits_the_second_it_is_obs_disconnected_and_nothing_is_sent(ObsLinkState link)
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await StartAsync(RecordingState.Stopped, clock);
        await session.Obs.StartRecordingAsync();
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording);

        Task<StepResult> running = StepOf(session, clock).RunAsync(Do("stop"), Chain, CancellationToken.None);
        await Poll.Until(() => session.Log.Recent().Any(entry => entry.Message.Contains("(E4-11)", StringComparison.Ordinal)));
        session.State.Update(s => s with { Obs = s.Obs.WithLink(link) });
        clock.Advance(ObsRecord.Settle);
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
        Assert.Equal("Sin OBS: no se mandó el pedido de parar.", result.Failure?.Message);
        Assert.Equal(0, session.Obs.Times("StopRecord"));
    }
}
