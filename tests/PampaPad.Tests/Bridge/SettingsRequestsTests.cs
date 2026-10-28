using System.Text.Json;
using System.Text.Json.Nodes;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>settings.save, update.check y update.skip por el puente, con el arranque y la actualización de mentira (E8-6).</summary>
public sealed class SettingsRequestsTests : IAsyncLifetime
{
    private readonly TempFolder _temp = new();
    private readonly StateStore _state = new();
    private readonly Log _log;
    private readonly LocalSettings _local;
    private readonly FakeStartup _startup = new(false);
    private int _checks;
    private int _skips;

    public SettingsRequestsTests()
    {
        _log = new Log(_temp.Combine("log"), TimeProvider.System);
        _local = new LocalSettings(LocalPath, _log);
    }

    private string LocalPath => _temp.Combine("local.json");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _state.DisposeAsync();
        await _log.DisposeAsync();
        _temp.Dispose();
    }

    [Fact]
    public async Task Start_with_windows_calls_the_startup_with_the_value_and_answers_the_new_settings()
    {
        Bridge bridge = Installed();

        using JsonDocument reply = await AskAsync(bridge, "settings.save", """{"startWithWindows":true}""");

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        Assert.Equal([true], _startup.Calls);
        Assert.Equal("on", reply.RootElement.GetProperty("data").GetProperty("startWithWindows").GetString());
    }

    [Theory]
    [InlineData("settings.save", """{"startWithWindows":true}""")]
    [InlineData("update.check", "{}")]
    [InlineData("update.skip", "{}")]
    public async Task Without_an_installed_copy_the_startup_and_the_update_are_refused(string request, string data)
    {
        var dev = new FakeStartup(null);
        Bridge bridge = Make(new SettingsSources(_local, dev, null, null, () => View(dev)));

        using JsonDocument reply = await AskAsync(bridge, request, data);

        Assert.Equal(ErrorCodes.InvalidData, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("copia de desarrollo", reply.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(dev.Calls);
    }

    [Fact]
    public async Task Clearing_the_skipped_version_leaves_the_rest_of_local_json_as_it_was()
    {
        File.WriteAllText(LocalPath, """{ "version": 1, "skippedVersion": "0.7.0", "consolePort": "COM7", "inventado": { "a": 1 } }""");
        Assert.True(_local.Load().Ok);
        Bridge bridge = Installed();

        using JsonDocument reply = await AskAsync(bridge, "settings.save", """{"skippedVersion":""}""");
        JsonNode saved = JsonNode.Parse(File.ReadAllText(LocalPath))!;

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        Assert.Null(saved["skippedVersion"]);
        Assert.Equal("COM7", saved["consolePort"]!.GetValue<string>());
        Assert.Equal(1, saved["inventado"]!["a"]!.GetValue<int>());
        Assert.Empty(_startup.Calls);
        Assert.False(reply.RootElement.GetProperty("data").TryGetProperty("skippedVersion", out _));
    }

    [Fact]
    public async Task Update_check_and_update_skip_call_their_functions()
    {
        Bridge bridge = Installed();

        using JsonDocument check = await AskAsync(bridge, "update.check", "{}");
        using JsonDocument skip = await AskAsync(bridge, "update.skip", "{}");

        Assert.True(check.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(skip.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, _checks);
        Assert.Equal(1, _skips);
    }

    [Theory]
    [InlineData("settings.save", """{"startWithWindows":true}""")]
    [InlineData("update.check", "{}")]
    [InlineData("update.skip", "{}")]
    public async Task The_mcp_gets_no_permission_even_with_the_mcp_allowed(string request, string data)
    {
        Bridge bridge = Installed();

        using JsonDocument reply = JsonDocument.Parse(await bridge.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", Caller.Mcp, CancellationToken.None));

        Assert.Equal(ErrorCodes.NoPermission, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_startup.Calls);
        Assert.Equal(0, _checks + _skips);
    }

    private Bridge Installed() => Make(new SettingsSources(
        _local,
        _startup,
        _ => { Interlocked.Increment(ref _checks); return Task.FromResult(Result.Success); },
        _ => { Interlocked.Increment(ref _skips); return Task.FromResult(Result.Success); },
        () => View(_startup)));

    private SettingsView View(IStartup startup) =>
        new(false, _local.Current.ConsolePort, _local.Current.SkippedVersion, SettingsView.Startup(startup.IsEnabled), new ObsSettingsView("127.0.0.1:4455"));

    private Bridge Make(SettingsSources sources)
    {
        var bridge = new Bridge(_state, _log) { McpAllowed = () => true };
        SettingsRequests.Register(bridge, sources);
        return bridge;
    }

    private static async Task<JsonDocument> AskAsync(Bridge bridge, string request, string data) =>
        JsonDocument.Parse(await bridge.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", Caller.Window, CancellationToken.None));
}
