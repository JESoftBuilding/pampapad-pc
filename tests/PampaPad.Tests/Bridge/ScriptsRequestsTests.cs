using System.Text.Json;
using PampaPad.Core;
using CoreAction = PampaPad.Core.Action;

namespace PampaPad.Tests;

/// <summary>scripts.list y scripts.read por el puente, con scripts de mentira y un despachador con el paso script.run de verdad (E8-3).</summary>
public sealed class ScriptsRequestsTests : IAsyncLifetime
{
    private readonly ScriptRig _rig = new();
    private readonly StateStore _state = new();
    private readonly ScriptRuns _runs;
    private readonly Dispatcher _dispatcher;
    private readonly Bridge _bridge;

    public ScriptsRequestsTests()
    {
        _runs = new ScriptRuns(_rig.Log, Path.Combine(_rig.LibraryFolder, "no-hay-registro"), TimeProvider.System);
        var executor = new Executor([new ScriptRun(_rig.Runner, _rig.Library)], [], TimeProvider.System);
        _dispatcher = new Dispatcher(_rig.Library, _state, executor, new Gestures(TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness), _rig.Log);
        _bridge = new Bridge(_state, _rig.Log);
        ScriptsRequests.Register(_bridge, new ScriptsSources(_rig.Library, _rig.Runner, _runs, new FakeShellDialogs(), Path.Combine(_rig.LibraryFolder, "respaldo"), TimeProvider.System));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _dispatcher.CancelAll();
        _runs.Dispose();
        await _state.DisposeAsync();
        await _rig.DisposeAsync();
    }

    [Fact]
    public async Task A_script_that_a_draft_is_running_is_listed_as_running()
    {
        string folder = _rig.AddPs1("lento", ScriptRig.LaunchesPing, """, "timeoutSec": 30""");
        var draft = new CoreAction { Name = "Prueba", Steps = [Step("lento")] };

        Assert.Null(_dispatcher.LaunchDraft(draft, new Invocation(Guid.NewGuid(), Trigger.Window, "window", null, null, null)));
        await ScriptRig.ChildAsync(folder);
        JsonElement running = (await AskAsync("scripts.list", "{}")).GetProperty("scripts")[0];
        _dispatcher.CancelAll();
        await Poll.Until(() => _rig.Runner.Running.Count == 0, 20_000);
        JsonElement after = (await AskAsync("scripts.list", "{}")).GetProperty("scripts")[0];

        Assert.Equal("running", running.GetProperty("status").GetString());
        Assert.Equal("ready", after.GetProperty("status").GetString());
        Assert.False(after.GetProperty("lastRun").GetProperty("end").GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Read_brings_the_files_the_fingerprint_and_the_last_runs()
    {
        string folder = _rig.AddCmd("hola", "@echo off\necho lista\n", """, "inventado": 1""");
        File.WriteAllText(Path.Combine(folder, "ayuda.ps1"), "Write-Output 'x'");
        await _rig.RunAsync("hola");
        await _rig.RunAsync("hola");

        JsonElement data = await AskAsync("scripts.read", """{"id":"hola"}""");

        Assert.Equal("hola", data.GetProperty("script").GetProperty("id").GetString());
        Assert.Equal(["ayuda.ps1", "hola.cmd", "script.json"], data.GetProperty("files").EnumerateArray().Select(file => file.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
        Assert.Equal(ScriptFingerprint.Of(folder), data.GetProperty("fingerprint").GetString());
        Assert.Equal(1, data.GetProperty("manifest").GetProperty("inventado").GetInt32());
        Assert.Equal(2, data.GetProperty("runs").GetArrayLength());
        Assert.Equal("lista", data.GetProperty("runs")[0].GetProperty("end").GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("suelto.cmd", ErrorCodes.ScriptWithoutManifest)]
    [InlineData("noesta", ErrorCodes.ScriptNotFound)]
    [InlineData("..", ErrorCodes.ScriptNotFound)]
    public async Task Read_of_a_loose_file_or_of_nothing_fails_with_its_code(string id, string code)
    {
        File.WriteAllText(Path.Combine(_rig.LibraryFolder, "scripts", "suelto.cmd"), "@echo off");

        using JsonDocument reply = JsonDocument.Parse(await _bridge.HandleAsync(
            $$$"""{"id":1,"request":"scripts.read","data":{"id":"{{{id}}}"}}""", Caller.Window, CancellationToken.None));

        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(code, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static Step Step(string script) =>
        JsonSerializer.Deserialize<Step>($$"""{ "type": "script.run", "script": "{{script}}" }""", Json.Options)!;

    private async Task<JsonElement> AskAsync(string request, string data)
    {
        using JsonDocument reply = JsonDocument.Parse(await _bridge.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", Caller.Window, CancellationToken.None));
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        return reply.RootElement.GetProperty("data").Clone();
    }
}
