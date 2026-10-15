using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ObsRecordTests
{
    private static readonly Invocation Key = new(Guid.NewGuid(), Trigger.Key, "console", "key.12", Gesture.Tap, 0);

    private static Step Do(string command) => JsonSerializer.Deserialize<Step>($$"""{ "type": "obs.record", "do": "{{command}}" }""", Json.Options)!;

    private static ObsRecord StepOf(ObsSession session, TimeProvider? clock = null) =>
        new(session.Client, session.State, session.Log, clock ?? TimeProvider.System);

    private static void Pretend(ObsSession session, RecordingState recording) =>
        session.State.Update(s => s with { Obs = s.Obs with { Recording = recording } });

    [Fact]
    public async Task Start_from_stopped_sends_StartRecord_and_the_state_follows_the_event()
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await StepOf(session).RunAsync(Do("start"), Key, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times("StartRecord"));
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording);
    }

    [Fact]
    public async Task Start_while_recording_sends_nothing_ends_well_and_leaves_a_line()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        Invocation key = Key with { RunId = Guid.NewGuid() };

        StepResult result = await StepOf(session).RunAsync(Do("start"), key, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(0, session.Obs.Times("StartRecord"));
        LogEntry line = Assert.Single(session.Log.Recent(), entry => entry.RunId == key.RunId);
        Assert.Equal("grabar: OBS ya está grabando, no se manda nada.", line.Message);
        Assert.Equal(Trigger.Key, line.RequestedBy);
    }

    [Fact]
    public async Task Stop_from_pause_sends_StopRecord_directly()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Paused);

        StepResult result = await StepOf(session).RunAsync(Do("stop"), Key, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times("StopRecord"));
        Assert.Equal(0, session.Obs.Times("ResumeRecord"));           // (Medido: E0-1) desde la pausa se detiene sin reanudar
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Stopped);
    }

    [Theory]
    [InlineData("pause", "PauseRecord")]
    [InlineData("resume", "ResumeRecord")]
    public async Task Pause_or_resume_with_nothing_recording_is_not_recording_and_sends_nothing(string command, string request)
    {
        await using ObsSession session = await ObsSession.StartAsync();

        StepResult result = await StepOf(session).RunAsync(Do(command), Key, CancellationToken.None);

        Assert.Equal(ErrorCodes.NotRecording, result.Failure?.Code);
        Assert.Equal(0, session.Obs.Times(request));
    }

    [Fact]
    public async Task Pause_and_resume_follow_obs_and_a_second_pause_sends_nothing()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        ObsRecord record = StepOf(session);

        Assert.Null((await record.RunAsync(Do("pause"), Key, CancellationToken.None)).Failure);
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Paused);
        Assert.Null((await record.RunAsync(Do("pause"), Key, CancellationToken.None)).Failure);
        Assert.Null((await record.RunAsync(Do("resume"), Key, CancellationToken.None)).Failure);
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording);

        Assert.Equal(1, session.Obs.Times("PauseRecord"));
        Assert.Equal(1, session.Obs.Times("ResumeRecord"));
    }

    [Fact]
    public async Task If_obs_was_already_recording_the_500_of_the_race_counts_as_done()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        Pretend(session, RecordingState.Stopped);                     // el programa todavía no se enteró de que OBS graba

        StepResult result = await StepOf(session).RunAsync(Do("start"), Key, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times("StartRecord"));
        Assert.Contains(session.Log.Recent(), entry => entry.Message == "grabar: OBS contestó 500, ya estaba así: cuenta como hecho.");
    }

    [Fact]
    public async Task While_starting_it_waits_for_the_event_and_decides_with_what_is_left()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        Pretend(session, RecordingState.Starting);

        Task<StepResult> running = StepOf(session).RunAsync(Do("start"), Key, CancellationToken.None);
        Assert.False(running.IsCompleted);
        Pretend(session, RecordingState.Recording);                  // llega el STARTED
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(result.Failure);
        Assert.Equal(0, session.Obs.Times("StartRecord"));
    }

    [Fact]
    public async Task While_stopping_it_waits_and_then_sends_what_is_needed()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        Pretend(session, RecordingState.Stopping);

        Task<StepResult> running = StepOf(session).RunAsync(Do("start"), Key, CancellationToken.None);
        Pretend(session, RecordingState.Stopped);                    // llega el STOPPED
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(result.Failure);
        Assert.Equal(1, session.Obs.Times("StartRecord"));
    }

    [Fact]
    public async Task If_the_transition_never_ends_it_is_obs_timeout_at_two_seconds_and_nothing_is_sent()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        var clock = new FakeTimeProvider();
        Pretend(session, RecordingState.Starting);

        Task<StepResult> running = StepOf(session, clock).RunAsync(Do("stop"), Key, CancellationToken.None);
        clock.Advance(ObsClient.RequestTimeout);
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ErrorCodes.ObsTimeout, result.Failure?.Code);
        Assert.Equal(0, session.Obs.Times("StopRecord"));
    }

    [Fact]
    public async Task A_real_start_of_the_fake_obs_is_waited_for()
    {
        // StartDelay no tenía prueba (hallazgo E2-2). Medido: 90 ms y 142 ms; acá 300 ms, para que el paso llegue a esperarlo.
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Stopped, obs => obs.StartDelay = TimeSpan.FromMilliseconds(300));
        Task fromObs = session.Obs.StartRecordingAsync();            // como desde la ventana de OBS: STARTING ya, STARTED a los 300 ms
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Starting);

        StepResult result = await StepOf(session).RunAsync(Do("start"), Key, CancellationToken.None);
        await fromObs;

        Assert.Null(result.Failure);
        Assert.Equal(RecordingState.Recording, session.State.Current.Obs.Recording);
        Assert.Equal(0, session.Obs.Times("StartRecord"));
    }

    [Fact]
    public async Task A_real_stop_of_the_fake_obs_is_waited_for()
    {
        // StopDelay tampoco tenía prueba (hallazgo E2-2). Medido: 310 ms y 311 ms.
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording, obs => obs.StopDelay = TimeSpan.FromMilliseconds(300));
        Task fromObs = session.Obs.StopRecordingAsync();
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Stopping);

        StepResult result = await StepOf(session).RunAsync(Do("stop"), Key, CancellationToken.None);
        await fromObs;

        Assert.Null(result.Failure);
        Assert.Equal(0, session.Obs.Times("StopRecord"));
    }

    [Fact]
    public async Task Without_a_link_nothing_is_sent()
    {
        await using var offline = new OfflineObs();
        var record = new ObsRecord(offline.Client, offline.State, offline.Log, TimeProvider.System);

        StepResult result = await record.RunAsync(Do("start"), Key, CancellationToken.None);

        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
        Assert.Equal("Sin OBS: no se mandó el pedido de grabar.", result.Failure?.Message);
    }

    [Theory]
    [InlineData("""{ "type": "obs.record", "do": "grabar" }""", "do")]
    [InlineData("""{ "type": "obs.record", "do": "Start" }""", "do")]
    [InlineData("""{ "type": "obs.record" }""", "do")]
    [InlineData("""{ "type": "obs.record", "do": 1 }""", "")]
    public async Task An_order_that_is_not_one_of_the_four_is_a_problem_of_the_step(string json, string where)
    {
        await using var offline = new OfflineObs();
        var record = new ObsRecord(offline.Client, offline.State, offline.Log, TimeProvider.System);

        Problem problem = Assert.Single(record.Validate(JsonSerializer.Deserialize<Step>(json, Json.Options)!));

        Assert.Equal(where, problem.Where);
        Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
    }
}
