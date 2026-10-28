using System.Text.Json;
using System.Text.Json.Nodes;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// El interruptor "Permitir que Claude maneje PampaPad" (E-MCP-3): local.json mcpEnabled, null es encendido. El puente lo lee de
/// LocalSettings en cada pedido, como lo arma la composición, así que apagarlo vale en el acto.
/// </summary>
public sealed class McpSwitchTests : IAsyncLifetime
{
    private readonly TempFolder _temp = new();
    private readonly StateStore _state = new();
    private readonly Log _log;
    private readonly LocalSettings _local;
    private readonly Bridge _bridge;

    public McpSwitchTests()
    {
        _log = new Log(_temp.Combine("log"), TimeProvider.System);
        _local = new LocalSettings(LocalPath, _log);
        _bridge = new Bridge(_state, _log) { McpAllowed = _local.McpAllowed };
        SettingsRequests.Register(_bridge, new SettingsSources(
            _local, new FakeStartup(false), null, null, () => new SettingsView(false, null, null, McpEnabled: _local.McpAllowed())));
        _bridge.Register<NoData, NoData>("test.read", RequestKind.Read, Doors.WindowAndMcp, (_, _, _) => Task.FromResult(Result<NoData>.Success(new NoData())));
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
    public async Task Without_the_field_the_mcp_passes()
    {
        File.WriteAllText(LocalPath, """{ "version": 1 }""");
        Assert.True(_local.Load().Ok);

        Assert.True(Ok(await AskAsync("test.read", "{}", Caller.Mcp)));
    }

    [Fact]
    public async Task Switched_off_the_mcp_gets_no_permission_even_to_read()
    {
        File.WriteAllText(LocalPath, """{ "version": 1, "mcpEnabled": false }""");
        Assert.True(_local.Load().Ok);

        using JsonDocument reply = await AskAsync("test.read", "{}", Caller.Mcp);

        Assert.Equal(ErrorCodes.NoPermission, Code(reply));
        Assert.Equal(BridgePolicy.McpOff, reply.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    // Revisión de la tanda 1 (must): un local.json que no se lee deja los valores por defecto, y el del MCP es encendido. Falla cerrado.
    [Theory]
    [InlineData("""{ "version": 1, "mcpEnabled": "false" }""")]
    [InlineData("""{ "version": 99, "mcpEnabled": false }""")]
    [InlineData("""{ "version": 1, "mcpEnabled": fal""")]
    public async Task An_unreadable_local_json_switches_the_mcp_off(string text)
    {
        File.WriteAllText(LocalPath, text);
        Assert.False(_local.Load().Ok);

        using JsonDocument reply = await AskAsync("test.read", "{}", Caller.Mcp);

        Assert.Equal(ErrorCodes.NoPermission, Code(reply));
    }

    [Fact]
    public async Task Changing_it_from_the_window_counts_on_the_next_request_without_restarting()
    {
        using JsonDocument off = await AskAsync("settings.save", """{"mcpEnabled":false}""", Caller.Window);
        using JsonDocument refused = await AskAsync("test.read", "{}", Caller.Mcp);
        using JsonDocument on = await AskAsync("settings.save", """{"mcpEnabled":true}""", Caller.Window);
        using JsonDocument passes = await AskAsync("test.read", "{}", Caller.Mcp);

        Assert.False(off.RootElement.GetProperty("data").GetProperty("mcpEnabled").GetBoolean());
        Assert.Equal(ErrorCodes.NoPermission, Code(refused));
        Assert.True(on.RootElement.GetProperty("data").GetProperty("mcpEnabled").GetBoolean());
        Assert.True(Ok(passes));
    }

    [Fact]
    public async Task The_mcp_cannot_switch_itself_on_or_off()
    {
        using JsonDocument reply = await AskAsync("settings.save", """{"mcpEnabled":true}""", Caller.Mcp);

        Assert.Equal(ErrorCodes.NoPermission, Code(reply));
        Assert.False(File.Exists(LocalPath));
    }

    [Fact]
    public async Task Saving_it_keeps_what_this_version_does_not_know_in_local_json()
    {
        File.WriteAllText(LocalPath, """{ "version": 1, "consolePort": "COM7", "inventado": { "a": 1 } }""");
        Assert.True(_local.Load().Ok);

        Assert.True(Ok(await AskAsync("settings.save", """{"mcpEnabled":false}""", Caller.Window)));
        JsonNode saved = JsonNode.Parse(File.ReadAllText(LocalPath))!;

        Assert.False(saved["mcpEnabled"]!.GetValue<bool>());
        Assert.Equal("COM7", saved["consolePort"]!.GetValue<string>());
        Assert.Equal(1, saved["inventado"]!["a"]!.GetValue<int>());
    }

    [Fact]
    public void The_mcp_door_lets_in_only_the_mcp()
    {
        Assert.Equal([Caller.Mcp], Doors.Mcp);
        Assert.Equal(ErrorCodes.NoPermission, BridgePolicy.Check("x", RequestKind.Read, Doors.Mcp, Caller.Window, new PolicyContext(true, false))?.Code);
        Assert.Null(BridgePolicy.Check("x", RequestKind.Read, Doors.Mcp, Caller.Mcp, new PolicyContext(true, false)));
    }

    private static bool Ok(JsonDocument reply) => reply.RootElement.GetProperty("ok").GetBoolean();

    private static string? Code(JsonDocument reply) => reply.RootElement.GetProperty("error").GetProperty("code").GetString();

    private async Task<JsonDocument> AskAsync(string request, string data, Caller caller) =>
        JsonDocument.Parse(await _bridge.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", caller, CancellationToken.None));
}
