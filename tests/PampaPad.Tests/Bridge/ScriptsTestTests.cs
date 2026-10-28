using System.Collections.Concurrent;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>scripts.test con la salida en vivo por script.output, con scripts de mentira y el paso script.run de verdad (E8-5).</summary>
public sealed class ScriptsTestTests : IAsyncLifetime
{
    private readonly ScriptRig _rig = new();
    private readonly StateStore _state = new();
    private readonly ScriptOutputSink _output = new();
    private readonly ConcurrentQueue<ScriptOutputEvent> _events = new();
    private readonly Dispatcher _dispatcher;
    private readonly Bridge _bridge;
    private readonly IDisposable _hooks;
    private readonly ScriptRun _step;
    private int _notices;

    public ScriptsTestTests()
    {
        _step = new ScriptRun(_rig.Runner, _rig.Library, _output);
        var executor = new Executor([_step], [], TimeProvider.System);
        _dispatcher = new Dispatcher(_rig.Library, _state, executor, new Gestures(TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness), _rig.Log);
        var notifier = new Notifier(_state, _rig.Library, _rig.Log, _ => Interlocked.Increment(ref _notices), _ => Interlocked.Increment(ref _notices));
        _dispatcher.Finished += notifier.OnFinished;
        _bridge = new Bridge(_state, _rig.Log) { McpAllowed = () => true };
        _hooks = Requests.RegisterAll(
            _bridge,
            new StatusSources("9.9.9", _state, _rig.Library, _dispatcher, _rig.Log, [_step], [], () => new SettingsView(false, null, null)),
            new EditSources(_rig.Library, _state, _rig.Log, executor, [_step], [], _dispatcher, new FakeWindows(), null));
        _output.Published += _events.Enqueue;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _hooks.Dispose();
        _dispatcher.CancelAll();
        await Poll.Until(() => _rig.Runner.Running.Count == 0, 20_000);
        await _state.DisposeAsync();
        await _rig.DisposeAsync();
    }

    [Fact]
    public async Task The_lines_arrive_in_order_with_the_run_id_an_error_line_is_marked_and_the_end_brings_the_exit_code()
    {
        _rig.AddCmd("hola", "@echo off\necho uno\necho dos\n>&2 echo fallo\necho tres\n");

        Guid runId = await TestAsync("hola");
        ScriptEnd end = await EndAsync(runId);

        ScriptOutputEvent[] lines = [.. _events.Where(e => e.End is null)];
        Assert.All(lines, e => Assert.Equal(runId, e.RunId));
        Assert.Equal(["uno", "dos", "tres"], lines.Where(e => e.Stream == ScriptOutputSink.Stdout).Select(e => e.Line));
        Assert.Equal("fallo", Assert.Single(lines, e => e.Stream == ScriptOutputSink.Stderr).Line);
        Assert.True(end.Ok);
        Assert.Equal(0, end.ExitCode);
        Assert.Equal("tres", end.Message);
    }

    [Fact]
    public async Task A_run_that_fails_also_sends_the_end_and_the_notifier_stays_quiet()
    {
        _rig.AddCmd("falla", "@echo off\necho se rompio\nexit /b 3\n");

        Guid runId = await TestAsync("falla");
        ScriptEnd end = await EndAsync(runId);
        await Poll.Until(() => _state.Current.Running.Count == 0);

        Assert.False(end.Ok);
        Assert.Equal(3, end.ExitCode);
        Assert.Equal(0, Volatile.Read(ref _notices));
    }

    // La puerta nueva no saltea la toma: ni la ventana ni el MCP corren nada mientras se graba (E-MCP-4).
    [Theory]
    [InlineData(Caller.Window)]
    [InlineData(Caller.Mcp)]
    public async Task With_a_take_in_progress_it_is_refused_and_nothing_runs(Caller caller)
    {
        string folder = _rig.AddCmd("escribe", "@echo off\necho x> \"%~dp0corrio.txt\"\n");
        _state.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });

        using JsonDocument reply = await AskAsync("scripts.test", """{"id":"escribe"}""", caller);
        await Task.Delay(300);

        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_events);
        Assert.False(File.Exists(Path.Combine(folder, "corrio.txt")));
    }

    [Theory]
    [InlineData("noesta", ErrorCodes.ScriptNotFound)]
    [InlineData("sinficha", ErrorCodes.ScriptWithoutManifest)]
    public async Task A_script_that_is_missing_or_has_no_manifest_is_refused_before_launching(string id, string code)
    {
        Directory.CreateDirectory(Path.Combine(_rig.LibraryFolder, "scripts", "sinficha"));

        using JsonDocument reply = await AskAsync("scripts.test", $$"""{"id":"{{id}}"}""");

        Assert.Equal(code, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_state.Current.Running);
    }

    [Fact]
    public async Task Action_cancel_with_the_run_id_cuts_the_run()
    {
        string folder = _rig.AddPs1("lento", ScriptRig.LaunchesPing, """, "timeoutSec": 30""");

        Guid runId = await TestAsync("lento");
        int child = await ScriptRig.ChildAsync(folder);
        using JsonDocument cancelled = await AskAsync("action.cancel", $$"""{"runId":"{{runId}}"}""");
        ScriptEnd end = await EndAsync(runId);

        Assert.Equal(1, cancelled.RootElement.GetProperty("data").GetProperty("cancelled").GetInt32());
        Assert.False(end.Ok);
        Assert.Null(end.ExitCode);
        Assert.True(ScriptRig.Gone(child));
    }

    [Fact]
    public async Task A_run_that_does_not_come_from_the_window_publishes_no_output()
    {
        _rig.AddCmd("hola", "@echo off\necho uno\n");
        var invocation = new Invocation(Guid.NewGuid(), Trigger.Key, "console", "key.3", Gesture.Press, 0, "ir-al-banco");

        Step step = JsonSerializer.Deserialize<Step>("""{ "type": "script.run", "script": "hola" }""", Json.Options)!;

        StepResult result = await _step.RunAsync(step, invocation, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task While_a_script_is_tested_an_action_test_is_refused_and_the_other_way_around()
    {
        string folder = _rig.AddPs1("lento", ScriptRig.LaunchesPing, """, "timeoutSec": 30""");
        string draft = """{"action":{"name":"Prueba","steps":[{"type":"script.run","script":"lento"}]}}""";

        Guid runId = await TestAsync("lento");
        await ScriptRig.ChildAsync(folder);
        using JsonDocument action = await AskAsync("action.test", draft);
        _dispatcher.Cancel(runId);
        await EndAsync(runId);
        await Poll.Until(() => _state.Current.Running.Count == 0 && _rig.Runner.Running.Count == 0, 20_000);
        File.Delete(Path.Combine(folder, "hijo.txt"));
        using JsonDocument first = await AskAsync("action.test", draft);
        await ScriptRig.ChildAsync(folder);
        using JsonDocument script = await AskAsync("scripts.test", """{"id":"lento"}""");

        Assert.Equal(ErrorCodes.Cancelled, action.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.True(first.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(ErrorCodes.Cancelled, script.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task From_the_mcp_it_answers_the_run_id_and_the_output_arrives_by_script_output()
    {
        _rig.AddCmd("hola", "@echo off\necho uno\necho dos\n");

        Guid runId = await TestAsync("hola", Caller.Mcp);
        ScriptEnd end = await EndAsync(runId);

        Assert.Equal(["uno", "dos"], _events.Where(e => e.RunId == runId && e.End is null).Select(e => e.Line));
        Assert.True(end.Ok);
        Assert.Contains(_rig.Log.Recent(), entry => entry.RequestedBy == Trigger.Mcp && entry.Message == "scripts.test: ok");
    }

    [Fact]
    public async Task A_run_from_the_mcp_that_fails_does_not_sound_and_leaves_no_last_failure()
    {
        _rig.AddCmd("falla", "@echo off\necho se rompio\nexit /b 3\n");

        Guid runId = await TestAsync("falla", Caller.Mcp);
        ScriptEnd end = await EndAsync(runId);
        await Poll.Until(() => _state.Current.Running.Count == 0);

        Assert.False(end.Ok);
        Assert.Equal(0, Volatile.Read(ref _notices));
        Assert.Null(_state.Current.LastFailure);
    }

    [Fact]
    public void The_sink_cuts_at_2000_lines_per_run_with_one_notice_and_still_sends_the_end()
    {
        var sink = new ScriptOutputSink();
        var published = new List<ScriptOutputEvent>();
        sink.Published += published.Add;
        var runId = Guid.NewGuid();

        for (int i = 0; i < ScriptOutputSink.MaxLines + 50; i++)
        {
            sink.Line(runId, $"linea {i}", error: false);
        }

        sink.End(runId, new ScriptEnd(true, 0, 10, "linea"));
        sink.Line(runId, "otra corrida con el mismo runId", error: true);

        Assert.Equal(ScriptOutputSink.MaxLines, published.Count(e => e.Stream == ScriptOutputSink.Stdout));
        Assert.Single(published, e => e.Stream == ScriptOutputSink.Notice);
        Assert.NotNull(published[^2].End);
        Assert.Equal(ScriptOutputSink.Stderr, published[^1].Stream);
    }

    private async Task<Guid> TestAsync(string id, Caller caller = Caller.Window)
    {
        using JsonDocument reply = await AskAsync("scripts.test", $$"""{"id":"{{id}}"}""", caller);
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        return reply.RootElement.GetProperty("data").GetProperty("runId").GetGuid();
    }

    private async Task<ScriptEnd> EndAsync(Guid runId)
    {
        await Poll.Until(() => _events.Any(e => e.RunId == runId && e.End is not null), 20_000);
        return _events.First(e => e.RunId == runId && e.End is not null).End!;
    }

    private async Task<JsonDocument> AskAsync(string request, string data, Caller caller = Caller.Window) =>
        JsonDocument.Parse(await _bridge.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", caller, CancellationToken.None));
}
