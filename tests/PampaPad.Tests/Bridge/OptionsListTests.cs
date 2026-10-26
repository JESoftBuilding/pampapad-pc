using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class OptionsListTests : IAsyncLifetime
{
    private readonly DispatcherRig _rig = new(DispatcherRig.Example);
    private readonly FakeWindows _windows = new();
    private readonly RealCatalog _catalog;
    private readonly Bridge _bridge;
    private IDisposable? _hooks;

    public OptionsListTests()
    {
        TestChains.CopyExampleScripts(_rig.Library.Folder);
        _catalog = new RealCatalog(_rig.Library);
        _bridge = new Bridge(_rig.State, _rig.Log);
        _hooks = Requests.RegisterAll(
            _bridge,
            new StatusSources("9.9.9", _rig.State, _rig.Library, _rig.Dispatcher, _rig.Log, [_rig.Marker], [], () => new SettingsView(true, null, null)),
            new EditSources(_rig.Library, _rig.State, _rig.Log, _catalog.Executor, _catalog.Steps, _catalog.Conditions, _rig.Dispatcher, _windows, _rig.Hotkeys));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _hooks?.Dispose();
        await _catalog.DisposeAsync();
        await _rig.DisposeAsync();
    }

    private async Task<JsonDocument> AskAsync(string data, Caller caller = Caller.Window) =>
        JsonDocument.Parse(await _bridge.HandleAsync($$"""{"id":1,"request":"options.list","data":{{data}}}""", caller, CancellationToken.None));

    private void ObsReady() => _rig.State.Update(s => s with
    {
        Obs = s.Obs with
        {
            Link = ObsLinkState.Ready,
            Scenes = [new ObsItem("uuid-camara", "Cámara"), new ObsItem("uuid-banco", "Banco")],
            Inputs = [new ObsItem("uuid-mic", "Mic/Aux")],
        },
    });

    private static (string? Name, string? Uuid)[] Items(JsonDocument reply) =>
        [.. reply.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Select(item =>
            (item.GetProperty("name").GetString(), item.TryGetProperty("uuid", out JsonElement uuid) ? uuid.GetString() : null))];

    [Fact]
    public async Task The_scenes_and_the_inputs_of_obs_come_with_their_uuid_in_the_order_obs_lists_them()
    {
        ObsReady();

        using JsonDocument scenes = await AskAsync("""{"list":"scenes"}""");
        using JsonDocument inputs = await AskAsync("""{"list":"inputs"}""");

        Assert.Equal(new (string?, string?)[] { ("Cámara", "uuid-camara"), ("Banco", "uuid-banco") }, Items(scenes));
        Assert.Equal(new (string?, string?)[] { ("Mic/Aux", "uuid-mic") }, Items(inputs));
        Assert.False(scenes.RootElement.GetProperty("data").TryGetProperty("unavailable", out _));
    }

    [Theory]
    [InlineData("scenes")]
    [InlineData("inputs")]
    public async Task Without_obs_the_list_is_empty_and_says_why_so_the_field_takes_text(string list)
    {
        _rig.State.Update(s => s with { Obs = s.Obs with { Link = ObsLinkState.Closed } });

        using JsonDocument reply = await AskAsync($$"""{"list":"{{list}}"}""");

        JsonElement data = reply.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("items").GetArrayLength());
        Assert.Contains("OBS no está conectado", data.GetProperty("unavailable").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_scripts_are_the_ones_with_a_ficha_and_carry_its_name()
    {
        using JsonDocument reply = await AskAsync("""{"list":"scripts"}""");

        JsonElement item = Assert.Single(reply.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("bancocam", item.GetProperty("name").GetString());
        Assert.Equal("Cámara del banco", item.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_windows_are_listed_once_per_title_with_their_process()
    {
        _windows.Open(new WindowInfo("bancocam", "scrcpy", 1823, 850, false));
        _windows.Open(new WindowInfo("OBS 32.2", "obs64", 1600, 900, false));
        _windows.Open(new WindowInfo("bancocam", "scrcpy", 1823, 850, false));

        using JsonDocument reply = await AskAsync("""{"list":"windows"}""");

        JsonElement[] items = [.. reply.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()];
        Assert.Equal(new[] { "bancocam", "OBS 32.2" }, items.Select(item => item.GetProperty("name").GetString()));
        Assert.Equal(new[] { "scrcpy", "obs64" }, items.Select(item => item.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task A_list_that_does_not_exist_or_a_field_that_is_not_known_is_invalid_data_and_the_tray_has_no_door()
    {
        using JsonDocument number = await AskAsync("""{"list":7}""");
        using JsonDocument unknown = await AskAsync("""{"list":"scenes","todas":true}""");
        using JsonDocument tray = await AskAsync("""{"list":"scenes"}""", Caller.Tray);

        Assert.Equal(ErrorCodes.InvalidData, number.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(ErrorCodes.InvalidData, unknown.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(ErrorCodes.NoPermission, tray.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
