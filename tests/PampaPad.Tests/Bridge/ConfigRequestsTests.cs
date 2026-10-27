using System.Collections.Concurrent;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class ConfigRequestsTests : IAsyncLifetime
{
    private readonly DispatcherRig _rig = new(DispatcherRig.Example);
    private readonly ConcurrentQueue<string> _events = new();
    private readonly Bridge _bridge;
    private readonly RealCatalog _catalog;
    private IDisposable? _hooks;

    public ConfigRequestsTests()
    {
        _bridge = new Bridge(_rig.State, _rig.Log);

        // Con los scripts de la biblioteca de ejemplo: sin ellos el catálogo real avisa script_not_found de "ir al banco".
        TestChains.CopyExampleScripts(_rig.Library.Folder);
        _catalog = new RealCatalog(_rig.Library);
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

    private Config Current => _rig.Library.Current;

    private string ConfigText => File.ReadAllText(Path.Combine(_rig.Library.Folder, CoreLibraryFile));

    private const string CoreLibraryFile = "config.json";

    private async Task<JsonDocument> AskAsync(string request, object data, Caller caller = Caller.Window)
    {
        string reply = await _bridge.HandleAsync(Envelope.Request(1, request, data), caller, CancellationToken.None);
        return JsonDocument.Parse(reply);
    }

    private Task<JsonDocument> SaveAsync(Config config, string? fingerprint = null, Caller caller = Caller.Window) =>
        AskAsync(RequestNames.ConfigSave, new ConfigSaveRequest(config, fingerprint ?? _rig.Library.Fingerprint), caller);

    // Una página nueva "sala" con una tecla, y la acción que va a ella: lo que arma Ajustes en la etapa 8 y el MCP antes.
    private Config WithKey(string hotkey) => Current with
    {
        Pages = [Current.Pages![0] with { Keys = [.. Current.Pages[0].Keys!, new Key { Position = 2, Label = "Nueva", Action = "grabar", Hotkey = hotkey }] }, .. Current.Pages.Skip(1)],
    };

    [Fact]
    public async Task Saving_writes_the_file_and_answers_with_the_new_fingerprint_and_the_remaining_warnings()
    {
        string before = _rig.Library.Fingerprint;
        Config next = Current with { Pages = [Current.Pages![0] with { Name = "Toma" }, .. Current.Pages.Skip(1)] };

        using JsonDocument reply = await SaveAsync(next);

        JsonElement data = reply.RootElement.GetProperty("data");
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.NotEqual(before, data.GetProperty("fingerprint").GetString());
        Assert.Equal(_rig.Library.Fingerprint, data.GetProperty("fingerprint").GetString());
        Assert.Equal(0, data.GetProperty("problems").GetArrayLength());
        Assert.Contains("\"name\": \"Toma\"", ConfigText, StringComparison.Ordinal);
        Assert.Equal("Toma", _rig.Library.Current.Pages![0].Name);
    }

    [Fact]
    public async Task A_save_from_a_stale_fingerprint_is_config_changed_on_disk_and_writes_nothing()
    {
        string stale = _rig.Library.Fingerprint;
        string theirs = ConfigText.Replace("\"name\": \"Grabación\"", "\"name\": \"De otra copia\"", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(_rig.Library.Folder, CoreLibraryFile), theirs);

        using JsonDocument reply = await SaveAsync(Current with { MainPage = "escenas" }, stale);

        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(ErrorCodes.ConfigChangedOnDisk, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(theirs, ConfigText);
    }

    [Fact]
    public async Task An_error_stops_the_save_with_config_invalid_and_every_problem_with_its_place()
    {
        string before = ConfigText;
        Key broken = Current.Pages![0].Keys![0] with { Action = "no-existe", Position = 99 };
        Config next = Current with { Pages = [Current.Pages[0] with { Keys = [broken, .. Current.Pages[0].Keys!.Skip(1)] }, .. Current.Pages.Skip(1)] };

        using JsonDocument reply = await SaveAsync(next);

        JsonElement error = reply.RootElement.GetProperty("error");
        Assert.Equal(ErrorCodes.ConfigInvalid, error.GetProperty("code").GetString());
        string?[] where = [.. error.GetProperty("problems").EnumerateArray().Select(problem => problem.GetProperty("where").GetString())];
        Assert.Contains("pages[0].keys[0].action", where);
        Assert.Contains("pages[0].keys[0].position", where);
        Assert.Equal(where[0], error.GetProperty("where").GetString());
        Assert.Equal(before, ConfigText);
    }

    [Fact]
    public async Task A_page_go_to_a_page_the_same_save_adds_is_accepted_with_the_real_catalog()
    {
        // Hallazgo E5-11 de punta a punta: la página "sala" y el page.go a "sala" llegan juntos.
        Config next = Current with
        {
            Pages = [.. Current.Pages!, new Page { Id = "sala", Name = "Sala", Keys = [] }],
            Actions = [.. Current.Actions!, JsonSerializer.Deserialize<PampaPad.Core.Action>("""{ "id": "ir-a-sala", "name": "Ir a sala", "steps": [ { "type": "page.go", "target": "sala" } ] }""", Json.Options)!],
        };

        using JsonDocument reply = await SaveAsync(next);

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        Assert.Contains("\"id\": \"sala\"", ConfigText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_go_to_a_page_that_does_not_exist_stops_the_save()
    {
        Config next = Current with
        {
            Actions = [.. Current.Actions!, JsonSerializer.Deserialize<PampaPad.Core.Action>("""{ "id": "ir-a-ninguna", "name": "Ir a ninguna", "steps": [ { "type": "page.go", "target": "ninguna" } ] }""", Json.Options)!],
        };

        using JsonDocument reply = await SaveAsync(next);

        Assert.Equal(ErrorCodes.ConfigInvalid, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal($"actions[{Current.Actions!.Count}].steps[0].target", reply.RootElement.GetProperty("error").GetProperty("where").GetString());
    }

    [Fact]
    public async Task The_parameters_of_a_step_are_written_in_the_order_of_its_fields_even_if_the_page_sent_another()
    {
        string shuffled = """
            { "version": 1, "actions": [ { "id": "esperar", "name": "Esperar", "steps": [
              { "type": "wait.window", "process": "scrcpy", "timeoutSec": 5, "height": 850, "width": 1823, "title": "bancocam" }
            ] } ] }
            """;
        string request = $$$"""{"id":1,"request":"config.save","data":{"config":{{{shuffled}}},"expectedFingerprint":"{{{_rig.Library.Fingerprint}}}"}}""";

        using JsonDocument reply = JsonDocument.Parse(await _bridge.HandleAsync(request, Caller.Window, CancellationToken.None));

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        string text = ConfigText;
        int[] at = [.. new[] { "\"title\"", "\"width\"", "\"height\"", "\"timeoutSec\"", "\"process\"" }.Select(name => text.IndexOf(name, StringComparison.Ordinal))];
        Assert.Equal(at.Order(), at);
        Assert.All(at, position => Assert.True(position >= 0));
    }

    [Fact]
    public async Task A_combination_windows_refused_is_saved_and_comes_back_as_a_warning_of_its_key()
    {
        _rig.Windows.Taken.Add(Hotkey.Parse("Ctrl+Shift+Alt+F12")!.Value);

        using JsonDocument reply = await SaveAsync(WithKey("Ctrl+Shift+Alt+F12"));

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        JsonElement problems = reply.RootElement.GetProperty("data").GetProperty("problems");
        JsonElement taken = Assert.Single(problems.EnumerateArray(), problem => problem.GetProperty("code").GetString() == ErrorCodes.HotkeyTaken);
        Assert.Equal("pages[0].keys[" + (Current.Pages![0].Keys!.Count - 1) + "].hotkey", taken.GetProperty("where").GetString());
        Assert.Equal("warning", taken.GetProperty("severity").GetString());
        Assert.Contains("Ctrl+Shift+Alt+F12", ConfigText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_with_a_take_in_progress_is_refused_even_from_the_window_and_the_mcp_never_gets_in()
    {
        _rig.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });
        string before = ConfigText;

        using JsonDocument taking = await SaveAsync(Current);
        using JsonDocument mcp = await SaveAsync(Current, caller: Caller.Mcp);

        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, taking.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(ErrorCodes.NoPermission, mcp.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, ConfigText);
    }

    [Fact]
    public async Task A_field_the_request_does_not_know_is_invalid_data_but_one_inside_the_configuration_is_kept()
    {
        string extra = """{"id":1,"request":"config.save","data":{"config":{"version":1},"expectedFingerprint":"","sobra":1}}""";
        string withExtra = ConfigText.TrimEnd()[..^1].TrimEnd() + ", \"inventado\": \"se queda\" }";
        string kept = $$$"""{"id":2,"request":"config.save","data":{"config":{{{withExtra}}},"expectedFingerprint":"{{{_rig.Library.Fingerprint}}}"}}""";

        using JsonDocument unknown = JsonDocument.Parse(await _bridge.HandleAsync(extra, Caller.Window, CancellationToken.None));
        using JsonDocument accepted = JsonDocument.Parse(await _bridge.HandleAsync(kept, Caller.Window, CancellationToken.None));

        Assert.Equal(ErrorCodes.InvalidData, unknown.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.True(accepted.RootElement.GetProperty("ok").GetBoolean(), accepted.RootElement.ToString());
        Assert.Contains("\"inventado\": \"se queda\"", ConfigText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reload_reads_the_disk_again_and_the_event_brings_the_new_fingerprint()
    {
        string edited = ConfigText.Replace("\"name\": \"Grabación\"", "\"name\": \"A mano\"", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(_rig.Library.Folder, CoreLibraryFile), edited);

        using JsonDocument reply = await AskAsync(RequestNames.LibraryReload, new NoData(), Caller.Tray);

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        Assert.Equal(_rig.Library.Fingerprint, reply.RootElement.GetProperty("data").GetProperty("fingerprint").GetString());
        Assert.Equal("A mano", _rig.Library.Current.Pages![0].Name);
        using JsonDocument changed = JsonDocument.Parse(Assert.Single(_events, line => line.Contains("\"event\":\"config.changed\"", StringComparison.Ordinal)));
        Assert.Equal(_rig.Library.Fingerprint, changed.RootElement.GetProperty("data").GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task The_snapshot_and_the_event_after_a_save_carry_the_fingerprint_of_what_was_written()
    {
        using JsonDocument saved = await SaveAsync(Current with { Pages = [Current.Pages![0] with { Name = "Toma" }, .. Current.Pages.Skip(1)] });
        Assert.True(saved.RootElement.GetProperty("ok").GetBoolean(), saved.RootElement.ToString());

        using JsonDocument snapshot = await AskAsync(RequestNames.AppSnapshot, new NoData());

        Assert.Equal(_rig.Library.Fingerprint, snapshot.RootElement.GetProperty("data").GetProperty("fingerprint").GetString());
        Assert.Equal("Toma", snapshot.RootElement.GetProperty("data").GetProperty("config").GetProperty("pages")[0].GetProperty("name").GetString());
        Assert.Contains(_events, line => line.Contains("\"event\":\"config.changed\"", StringComparison.Ordinal) && line.Contains(_rig.Library.Fingerprint, StringComparison.Ordinal));
    }
}
