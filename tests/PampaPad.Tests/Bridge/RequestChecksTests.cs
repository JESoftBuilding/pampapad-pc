using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// La validación de entrada de los pedidos que escriben (E8-7): un campo obligatorio que falta o un null dentro de una lista contestan
/// invalid_data con dónde, antes del manejador, y no se escribe nada.
/// </summary>
public sealed class RequestChecksTests : IAsyncLifetime
{
    private const string Config = """{"version":1,"mainPage":"grabacion","pages":[{"id":"grabacion","name":"Grabación","keys":[]}],"actions":[]}""";

    private readonly DispatcherRig _rig = new(DispatcherRig.Example);
    private readonly TempFolder _temp = new();
    private readonly ScriptRuns _runs;
    private readonly Bridge _bridge;
    private readonly IDisposable _hooks;

    public RequestChecksTests()
    {
        _bridge = new Bridge(_rig.State, _rig.Log);
        _runs = new ScriptRuns(_rig.Log, _temp.Combine("log"), TimeProvider.System);
        var startup = new FakeStartup(false);
        _hooks = Requests.RegisterAll(
            _bridge,
            new StatusSources("9.9.9", _rig.State, _rig.Library, _rig.Dispatcher, _rig.Log, [_rig.Marker], [], () => new SettingsView(false, null, null)),
            new EditSources(_rig.Library, _rig.State, _rig.Log, _rig.Executor, [_rig.Marker], [], _rig.Dispatcher, new FakeWindows(), _rig.Hotkeys),
            new ScriptsSources(_rig.Library, new ScriptRunner(_rig.Library, _rig.Log, TimeProvider.System, null), _runs, new FakeShellDialogs(), _temp.Combine("respaldo"), TimeProvider.System),
            new SettingsSources(new LocalSettings(_temp.Combine("local.json"), _rig.Log), startup, null, null, () => new SettingsView(false, null, null, SettingsView.Startup(startup.IsEnabled))));
    }

    private string LibraryFolder => _rig.Library.Folder;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _hooks.Dispose();
        _runs.Dispose();
        await _rig.DisposeAsync();
        _temp.Dispose();
    }

    [Theory]
    [InlineData("config.save", """{"expectedFingerprint":"x"}""", "config")]
    [InlineData("config.save", $$"""{"config":{{Config}}}""", "expectedFingerprint")]
    [InlineData("config.save", """{"config":{"version":1,"actions":[null]},"expectedFingerprint":"x"}""", "config.actions[0]")]
    [InlineData("config.save", """{"config":{"version":1,"pages":[null]},"expectedFingerprint":"x"}""", "config.pages[0]")]
    [InlineData("config.save", """{"config":{"version":1,"pages":[{"id":"a","keys":[null]}]},"expectedFingerprint":"x"}""", "config.pages[0].keys[0]")]
    [InlineData("config.save", """{"config":{"version":1,"pages":[{"id":"a","keys":[{"position":1,"shortLabel":["A",null]}]}]},"expectedFingerprint":"x"}""", "config.pages[0].keys[0].shortLabel[1]")]
    [InlineData("config.save", """{"config":{"version":1,"actions":[{"id":"a","steps":[null]}]},"expectedFingerprint":"x"}""", "config.actions[0].steps[0]")]
    [InlineData("config.save", """{"config":{"version":1,"hotkeys":[null],"preflight":[]},"expectedFingerprint":"x"}""", "config.hotkeys[0]")]
    [InlineData("config.save", """{"config":{"version":1,"preflight":[null]},"expectedFingerprint":"x"}""", "config.preflight[0]")]
    [InlineData("action.test", "{}", "action")]
    [InlineData("action.test", """{"action":{"name":"Prueba","steps":[null]}}""", "action.steps[0]")]
    [InlineData("scripts.test", "{}", "id")]
    [InlineData("scripts.write", """{"manifest":{"entry":"x.cmd"},"expectedFingerprint":""}""", "id")]
    [InlineData("scripts.write", """{"id":"nuevo","expectedFingerprint":""}""", "manifest")]
    [InlineData("scripts.write", """{"id":"nuevo","manifest":{"entry":"x.cmd"}}""", "expectedFingerprint")]
    [InlineData("settings.save", "{}", "data")]
    public async Task A_missing_field_or_a_null_in_a_list_is_invalid_data_with_where_and_nothing_is_written(string request, string data, string where)
    {
        byte[] before = File.ReadAllBytes(Path.Combine(LibraryFolder, "config.json"));

        using JsonDocument reply = JsonDocument.Parse(await _bridge.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", Caller.Window, CancellationToken.None));

        JsonElement error = reply.RootElement.GetProperty("error");
        Assert.Equal(ErrorCodes.InvalidData, error.GetProperty("code").GetString());
        Assert.Equal(where, error.GetProperty("where").GetString());
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(LibraryFolder, "config.json")));
        Assert.False(Directory.Exists(Path.Combine(LibraryFolder, "scripts", "nuevo")));
        Assert.False(File.Exists(_temp.Combine("local.json")));
        Assert.Empty(_rig.State.Current.Running);
    }
}
