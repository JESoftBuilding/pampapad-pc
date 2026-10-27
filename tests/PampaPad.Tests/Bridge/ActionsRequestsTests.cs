using System.Collections.Concurrent;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class ActionsRequestsTests : IAsyncLifetime
{
    private readonly DispatcherRig _rig = new(DispatcherRig.Example);
    private readonly ConcurrentQueue<string> _events = new();
    private readonly RealCatalog _catalog;
    private readonly Bridge _bridge;
    private IDisposable? _hooks;

    public ActionsRequestsTests()
    {
        _catalog = new RealCatalog(_rig.Library);
        _bridge = new Bridge(_rig.State, _rig.Log);
        _hooks = Requests.RegisterAll(
            _bridge,
            new StatusSources("9.9.9", _rig.State, _rig.Library, _rig.Dispatcher, _rig.Log, [_rig.Marker], [], () => new SettingsView(true, null, null)),
            new EditSources(_rig.Library, _rig.State, _rig.Log, _catalog.Executor, _catalog.Steps, _catalog.Conditions, _rig.Dispatcher, new FakeWindows(), _rig.Hotkeys));
        _bridge.Subscribe(_events.Enqueue);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _hooks?.Dispose();
        await _catalog.DisposeAsync();
        await _rig.DisposeAsync();
    }

    private const string Draft = """{ "id": "prueba", "name": "Prueba", "inventado": "se conserva", "steps": [ { "type": "obs.marker", "marker": "short" } ] }""";

    private async Task<JsonDocument> TestAsync(string data, Caller caller = Caller.Window) =>
        JsonDocument.Parse(await _bridge.HandleAsync($$"""{"id":1,"request":"action.test","data":{{data}}}""", caller, CancellationToken.None));

    private JsonElement Finished() => JsonDocument.Parse(Assert.Single(_events, line => line.Contains("\"event\":\"action.finished\"", StringComparison.Ordinal))).RootElement.GetProperty("data");

    [Fact]
    public async Task Testing_a_draft_answers_right_away_with_the_run_id_and_the_result_comes_by_the_event()
    {
        System.Action release = _rig.Hold();

        using JsonDocument reply = await TestAsync($$"""{"action":{{Draft}}}""");
        string runId = reply.RootElement.GetProperty("data").GetProperty("runId").GetString()!;
        bool running = _rig.State.Current.Running.Any(run => run.RunId.ToString() == runId);
        Assert.DoesNotContain(_events, line => line.Contains("action.finished", StringComparison.Ordinal));
        release();
        await Poll.Until(() => _events.Any(line => line.Contains("action.finished", StringComparison.Ordinal)));

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(running);
        JsonElement finished = Finished();
        Assert.Equal(runId, finished.GetProperty("runId").GetString());
        Assert.Equal("window", finished.GetProperty("requestedBy").GetString());
        Assert.True(finished.GetProperty("ok").GetBoolean());
        Assert.Equal("ok", finished.GetProperty("steps")[0].GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task A_draft_that_does_not_validate_still_answers_with_the_run_id_and_finishes_as_config_invalid()
    {
        using JsonDocument reply = await TestAsync("""{"action":{"id":"rota","name":"Rota","steps":[{"type":"no.existe"}]}}""");
        await Poll.Until(() => _events.Any(line => line.Contains("action.finished", StringComparison.Ordinal)));

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        JsonElement finished = Finished();
        Assert.False(finished.GetProperty("ok").GetBoolean());
        Assert.Equal(ErrorCodes.ConfigInvalid, finished.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal("notRun", finished.GetProperty("steps")[0].GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task The_same_draft_twice_is_refused_while_the_first_runs()
    {
        System.Action release = _rig.Hold();

        using JsonDocument first = await TestAsync($$"""{"action":{{Draft}}}""");
        using JsonDocument second = await TestAsync($$"""{"action":{{Draft}}}""");
        release();
        await Poll.Until(() => _events.Any(line => line.Contains("action.finished", StringComparison.Ordinal)));

        Assert.True(first.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(ErrorCodes.Cancelled, second.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_test_in_course_is_cancelled_with_action_cancel_and_its_run_id()
    {
        System.Action release = _rig.Hold();
        using JsonDocument reply = await TestAsync($$"""{"action":{{Draft}}}""");
        string runId = reply.RootElement.GetProperty("data").GetProperty("runId").GetString()!;

        string cancelled = await _bridge.HandleAsync($$$"""{"id":2,"request":"action.cancel","data":{"runId":"{{{runId}}}"}}""", Caller.Window, CancellationToken.None);
        release();
        await Poll.Until(() => _events.Any(line => line.Contains("action.finished", StringComparison.Ordinal)));

        Assert.Equal("""{"id":2,"ok":true,"data":{"cancelled":1}}""", cancelled);
        Assert.Equal(ErrorCodes.Cancelled, Finished().GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task With_a_take_in_progress_the_test_is_refused_and_nothing_runs()
    {
        _rig.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Paused } });

        using JsonDocument reply = await TestAsync($$"""{"action":{{Draft}}}""");

        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_rig.Marker.Runs);
    }

    [Fact]
    public async Task The_mcp_has_no_door_to_action_test_and_a_field_that_is_not_known_is_invalid_data()
    {
        using JsonDocument mcp = await TestAsync($$"""{"action":{{Draft}}}""", Caller.Mcp);
        using JsonDocument unknown = await TestAsync($$"""{"action":{{Draft}},"sobra":1}""");

        Assert.Equal(ErrorCodes.NoPermission, mcp.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(ErrorCodes.InvalidData, unknown.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
