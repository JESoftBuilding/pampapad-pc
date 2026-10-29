using System.Text.Json;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>
/// action.run y preflight.run, los dos pedidos que sólo usa el MCP para ejecutar (E-MCP-5). Con wait.time y obs.request de verdad contra
/// el OBS falso; el chequeo previo, contra un OBS que no está.
/// </summary>
public sealed class ActionRunTests
{
    private const string Library = """
        {
          "version": 1,
          "pages": [ { "id": "grabacion", "name": "Grabación", "keys": [] } ],
          "actions": [
            { "id": "espera", "name": "Espera", "steps": [ { "type": "wait.time", "durationMs": 50 } ] },
            { "id": "falla", "name": "Falla", "steps": [ { "type": "wait.time", "durationMs": 10 }, { "type": "obs.request", "requestType": "NoExiste" } ] },
            { "id": "pide", "name": "Pide", "steps": [ { "type": "obs.request", "requestType": "GetSceneList" } ] },
            { "id": "larga", "name": "Larga", "steps": [ { "type": "wait.time", "durationMs": 30000 } ] }
          ]
        }
        """;

    [Fact]
    public async Task A_wait_answers_when_it_ends_with_its_duration_and_the_step_ok()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session.State, session.Log, session.Client);

        JsonElement data = Data(await rig.AskAsync("action.run", """{"id":"espera"}"""));

        Assert.True(data.GetProperty("ok").GetBoolean());
        Assert.Equal("mcp", data.GetProperty("requestedBy").GetString());
        Assert.True(data.GetProperty("durationMs").GetInt64() >= 40);
        Assert.Equal("ok", Assert.Single(data.GetProperty("steps").EnumerateArray()).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task A_failure_in_the_second_step_says_which_and_its_code()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session.State, session.Log, session.Client);

        JsonElement data = Data(await rig.AskAsync("action.run", """{"id":"falla"}"""));

        Assert.False(data.GetProperty("ok").GetBoolean());
        Assert.Equal(2, data.GetProperty("stepNumber").GetInt32());
        Assert.Equal("obs.request", data.GetProperty("stepType").GetString());
        Assert.Equal(ErrorCodes.ObsRequestFailed, data.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_obs_request_brings_its_data_in_its_step()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session.State, session.Log, session.Client);

        JsonElement data = Data(await rig.AskAsync("action.run", """{"id":"pide"}"""));

        JsonElement step = Assert.Single(data.GetProperty("steps").EnumerateArray());
        Assert.Equal("Pantalla", step.GetProperty("data").GetProperty("currentProgramSceneName").GetString());
    }

    [Fact]
    public async Task An_action_that_does_not_exist_is_config_invalid_at_the_id()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session.State, session.Log, session.Client);

        using JsonDocument reply = await rig.AskAsync("action.run", """{"id":"no-esta"}""");

        JsonElement error = reply.RootElement.GetProperty("error");
        Assert.Equal(ErrorCodes.ConfigInvalid, error.GetProperty("code").GetString());
        Assert.Equal("id", error.GetProperty("where").GetString());
    }

    [Fact]
    public async Task An_action_in_course_is_cancelled_and_does_not_run_twice()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session.State, session.Log, session.Client);
        Task<ActionResult> first = rig.Dispatcher.Launch("larga", new Invocation(Guid.NewGuid(), Trigger.Key, "console", "key.1", Gesture.Press, 0, "larga"));

        using JsonDocument reply = await rig.AskAsync("action.run", """{"id":"larga"}""");
        rig.Dispatcher.CancelAll();
        await first;

        Assert.Equal(ErrorCodes.Cancelled, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task With_a_take_in_progress_action_run_is_read_only_while_recording_and_nothing_runs()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        await using var rig = new Rig(session.State, session.Log, session.Client);

        int before = session.Obs.Times("GetSceneList");

        using JsonDocument reply = await rig.AskAsync("action.run", """{"id":"pide"}""");
        await Task.Delay(100);

        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, session.Obs.Times("GetSceneList"));
    }

    [Fact]
    public async Task Without_obs_preflight_run_answers_every_check_skipped_with_obs_disconnected()
    {
        await using var catalog = new RealCatalog();
        await using var rig = new Rig(catalog.State, catalog.Log, catalog.Obs, catalog.Conditions);

        JsonElement data = Data(await rig.AskAsync("preflight.run", "{}"));

        Assert.Equal("mcp", data.GetProperty("requestedBy").GetString());
        JsonElement[] checks = [.. data.GetProperty("checks").EnumerateArray()];
        Assert.Equal(Preflight.Defaults.Count, checks.Length);
        Assert.All(checks, check =>
        {
            Assert.Equal("skipped", check.GetProperty("outcome").GetString());
            Assert.Equal(ErrorCodes.ObsDisconnected, check.GetProperty("code").GetString());
        });
    }

    [Theory]
    [InlineData("action.run", """{"id":"espera"}""")]
    [InlineData("preflight.run", "{}")]
    public async Task From_the_window_both_are_no_permission(string request, string data)
    {
        await using var catalog = new RealCatalog();
        await using var rig = new Rig(catalog.State, catalog.Log, catalog.Obs, catalog.Conditions);

        using JsonDocument reply = await rig.AskAsync(request, data, Caller.Window);

        Assert.Equal(ErrorCodes.NoPermission, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(rig.State.Current.Preflight);
    }

    private static JsonElement Data(JsonDocument reply)
    {
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.GetRawText());
        return reply.RootElement.GetProperty("data").Clone();
    }

    /// <summary>La biblioteca de arriba, wait.time y obs.request de verdad, el despachador y el puente con el MCP encendido.</summary>
    private sealed class Rig : IAsyncDisposable
    {
        private readonly TempFolder _temp = new();

        public Rig(StateStore state, Log log, ObsClient obs, IReadOnlyList<ConditionType>? conditions = null)
        {
            State = state;
            Directory.CreateDirectory(_temp.Combine("biblioteca"));
            File.WriteAllText(_temp.Combine("biblioteca", "config.json"), Library);
            var library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
            library.Load();
            IStepType[] steps = [new WaitTime(TimeProvider.System), new ObsRequestStep(obs, state)];
            var executor = new Executor(steps, conditions ?? [], TimeProvider.System);
            Dispatcher = new Dispatcher(library, state, executor, new Gestures(TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness), log);
            var preflight = new Preflight(conditions ?? [], library, state, log, TimeProvider.System);
            Bridge = new Bridge(state, log) { McpAllowed = () => true };
            ActionsRequests.Register(Bridge, new EditSources(library, state, log, executor, steps, conditions ?? [], Dispatcher, new FakeWindows(), null, preflight));
        }

        public StateStore State { get; }

        public Dispatcher Dispatcher { get; }

        public Bridge Bridge { get; }

        public async Task<JsonDocument> AskAsync(string request, string data, Caller caller = Caller.Mcp) =>
            JsonDocument.Parse(await Bridge.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", caller, CancellationToken.None));

        public ValueTask DisposeAsync()
        {
            Dispatcher.CancelAll();
            _temp.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
