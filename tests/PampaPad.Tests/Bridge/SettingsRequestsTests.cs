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

    [Theory]
    [InlineData("COM20")]
    [InlineData(ConsolePorts.Auto)]
    public async Task The_console_port_is_saved_in_local_json_and_the_rest_stays_as_it_was(string port)
    {
        File.WriteAllText(LocalPath, """{ "version": 1, "skippedVersion": "0.7.0", "inventado": { "a": 1 } }""");
        Assert.True(_local.Load().Ok);
        Bridge bridge = Installed();

        using JsonDocument reply = await AskAsync(bridge, "settings.save", $$"""{"consolePort":"{{port}}"}""");
        JsonNode saved = JsonNode.Parse(File.ReadAllText(LocalPath))!;

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        Assert.Equal(port, saved["consolePort"]!.GetValue<string>());
        Assert.Equal("0.7.0", saved["skippedVersion"]!.GetValue<string>());
        Assert.Equal(1, saved["inventado"]!["a"]!.GetValue<int>());
        Assert.Equal(port, reply.RootElement.GetProperty("data").GetProperty("consolePort").GetString());
    }

    [Fact]
    public async Task An_empty_console_port_is_no_console_and_takes_the_key_out()
    {
        File.WriteAllText(LocalPath, """{ "version": 1, "consolePort": "COM8" }""");
        Assert.True(_local.Load().Ok);

        using JsonDocument reply = await AskAsync(Installed(), "settings.save", """{"consolePort":""}""");

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        Assert.Null(JsonNode.Parse(File.ReadAllText(LocalPath))!["consolePort"]);
        Assert.False(reply.RootElement.GetProperty("data").TryGetProperty("consolePort", out _));
    }

    [Fact]
    public async Task Console_ports_answers_what_the_shell_lists_and_with_no_ports_an_empty_list()
    {
        IReadOnlyList<ConsolePortView> listed = [new("COM8", "USB-Enhanced-SERIAL CH9102", "1A86:55D4"), new("COM20", "USB-Enhanced-SERIAL CH343", "1A86:55D3")];
        Bridge some = Make(new SettingsSources(_local, _startup, null, null, () => View(_startup), () => listed));
        Bridge none = Make(new SettingsSources(_local, _startup, null, null, () => View(_startup), () => []));

        using JsonDocument two = await AskAsync(some, "console.ports", "{}");
        using JsonDocument empty = await AskAsync(none, "console.ports", "{}");

        JsonElement ports = two.RootElement.GetProperty("data").GetProperty("ports");
        Assert.Equal(["COM8", "COM20"], ports.EnumerateArray().Select(p => p.GetProperty("port").GetString()));
        Assert.Equal("1A86:55D3", ports[1].GetProperty("usbId").GetString());
        Assert.Equal(0, empty.RootElement.GetProperty("data").GetProperty("ports").GetArrayLength());
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
    [InlineData("settings.save", """{"consolePort":"COM20"}""")]
    [InlineData("console.ports", "{}")]
    [InlineData("update.check", "{}")]
    [InlineData("update.skip", "{}")]
    public async Task The_mcp_gets_no_permission_even_with_the_mcp_allowed(string request, string data)
    {
        Bridge bridge = Installed();

        using JsonDocument reply = JsonDocument.Parse(await bridge.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", Caller.Mcp, CancellationToken.None));

        Assert.Equal(ErrorCodes.NoPermission, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_startup.Calls);
        Assert.Equal(0, _checks + _skips);
        Assert.False(File.Exists(LocalPath));
    }

    [Fact]
    public async Task Console_ports_is_only_for_the_window_and_the_tray_cannot_ask_it()
    {
        using JsonDocument reply = JsonDocument.Parse(await Installed().HandleAsync("""{"id":1,"request":"console.ports","data":{}}""", Caller.Tray, CancellationToken.None));

        Assert.Equal(ErrorCodes.NoPermission, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
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
