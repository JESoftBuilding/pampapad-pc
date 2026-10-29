using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>action.save y key.assign, los pedidos del MCP que cambian una pieza de config.json (E-MCP-6), sobre la biblioteca de ejemplo.</summary>
public sealed class ConfigEditsTests : IAsyncLifetime
{
    private readonly DispatcherRig _rig = new(DispatcherRig.Example);
    private readonly Bridge _bridge;
    private readonly RealCatalog _catalog;
    private readonly IDisposable _hooks;

    public ConfigEditsTests()
    {
        _bridge = new Bridge(_rig.State, _rig.Log) { McpAllowed = () => true };
        TestChains.CopyExampleScripts(_rig.Library.Folder);
        _catalog = new RealCatalog(_rig.Library);
        _hooks = Requests.RegisterAll(
            _bridge,
            new StatusSources("9.9.9", _rig.State, _rig.Library, _rig.Dispatcher, _rig.Log, [_rig.Marker], [], () => new SettingsView(true, null, null)),
            new EditSources(_rig.Library, _rig.State, _rig.Log, _catalog.Executor, _catalog.Steps, _catalog.Conditions, _rig.Dispatcher, new FakeWindows(), _rig.Hotkeys));
    }

    private string File_ => Path.Combine(_rig.Library.Folder, "config.json");

    private string ConfigText => File.ReadAllText(File_);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _hooks.Dispose();
        await _catalog.DisposeAsync();
        await _rig.DisposeAsync();
    }

    private const string NewAction = """{ "id": "nueva", "name": "Nueva", "inventado": "se conserva", "steps": [ { "type": "obs.marker", "marker": "short" } ] }""";

    private async Task<JsonDocument> AskAsync(string request, string data, Caller caller = Caller.Mcp, CancellationToken ct = default) =>
        JsonDocument.Parse(await _bridge.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", caller, ct));

    private Task<JsonDocument> SaveActionAsync(string action, string? fingerprint = null) =>
        AskAsync("action.save", $$"""{"action":{{action}},"expectedFingerprint":"{{fingerprint ?? _rig.Library.Fingerprint}}"}""");

    private Task<JsonDocument> AssignAsync(string fields, string? fingerprint = null) =>
        AskAsync("key.assign", $$"""{{fields[..^1]}},"expectedFingerprint":"{{fingerprint ?? _rig.Library.Fingerprint}}"}""");

    private static string? Code(JsonDocument reply) => reply.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetProperty("code").GetString() : null;

    // Lo que tiene que quedar igual: el texto de config.json sin el bloque de la acción o la tecla que se tocó.
    private static string Without(string text, string piece) => text.Replace(piece, "", StringComparison.Ordinal);

    [Fact]
    public async Task A_new_action_is_added_at_the_end_with_its_unknown_field_and_the_rest_of_the_file_stays_byte_for_byte()
    {
        string before = ConfigText;

        using JsonDocument reply = await SaveActionAsync(NewAction);

        Assert.Null(Code(reply));
        Assert.Equal(_rig.Library.Fingerprint, reply.RootElement.GetProperty("data").GetProperty("fingerprint").GetString());
        PampaPad.Core.Action added = _rig.Library.Current.Actions![^1];
        Assert.Equal("nueva", added.Id);
        Assert.Equal("se conserva", added.Extra["inventado"].GetString());
        Assert.StartsWith(before[..before.LastIndexOf("    }\n  ],\n  \"hotkeys\"", StringComparison.Ordinal)], ConfigText, StringComparison.Ordinal);
        Assert.EndsWith(before[before.IndexOf("  \"hotkeys\"", StringComparison.Ordinal)..], ConfigText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_existing_action_is_replaced_in_its_place_and_nothing_else_changes()
    {
        int count = _rig.Library.Current.Actions!.Count;
        string before = ConfigText;

        using JsonDocument reply = await SaveActionAsync("""{ "id": "mic", "name": "Micrófono nuevo", "steps": [ { "type": "obs.marker", "marker": "short" } ] }""");

        Assert.Null(Code(reply));
        Assert.Equal(count, _rig.Library.Current.Actions!.Count);
        Assert.Equal("Micrófono nuevo", _rig.Library.Current.FindAction("mic")!.Name);
        string start = before[..before.IndexOf("      \"id\": \"mic\"", StringComparison.Ordinal)];
        string end = before[before.IndexOf("      \"id\": \"ir-al-banco\"", StringComparison.Ordinal)..];
        Assert.StartsWith(start, ConfigText, StringComparison.Ordinal);
        Assert.EndsWith(end, ConfigText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stale_fingerprint_writes_nothing_even_if_the_disk_matches_it()
    {
        string stale = _rig.Library.Fingerprint;
        await SaveActionAsync(NewAction);
        string after = ConfigText;

        using JsonDocument reply = await SaveActionAsync(NewAction.Replace("Nueva", "Otra", StringComparison.Ordinal), stale);

        Assert.Equal(ErrorCodes.ConfigChangedOnDisk, Code(reply));
        Assert.Equal(after, ConfigText);
    }

    [Fact]
    public async Task The_fingerprint_is_compared_against_the_disk_not_against_memory()
    {
        string read = _rig.Library.Fingerprint;
        string theirs = ConfigText.Replace("\"name\": \"Grabación\"", "\"name\": \"A mano\"", StringComparison.Ordinal);
        File.WriteAllText(File_, theirs);

        using JsonDocument action = await SaveActionAsync(NewAction, read);
        using JsonDocument key = await AssignAsync("""{"page":"grabacion","position":1,"label":"Uno","action":"mic"}""", read);

        Assert.Equal(ErrorCodes.ConfigChangedOnDisk, Code(action));
        Assert.Equal(ErrorCodes.ConfigChangedOnDisk, Code(key));
        Assert.Equal(theirs, ConfigText);
    }


    // El disco cambió y quien pide trae su huella, pero la memoria es la de antes: aplicar el cambio sobre la memoria pisaría lo del disco.
    [Fact]
    public async Task A_fingerprint_of_the_disk_that_memory_has_not_loaded_writes_nothing()
    {
        string theirs = ConfigText.Replace("\"name\": \"Grabación\"", "\"name\": \"A mano\"", StringComparison.Ordinal);
        File.WriteAllText(File_, theirs);

        using JsonDocument reply = await SaveActionAsync(NewAction, Fingerprints.Of(File.ReadAllBytes(File_)));

        Assert.Equal(ErrorCodes.ConfigChangedOnDisk, Code(reply));
        Assert.Equal(theirs, ConfigText);
    }
    [Fact]
    public async Task An_invalid_action_comes_back_with_its_problems_and_where_and_writes_nothing()
    {
        string before = ConfigText;

        using JsonDocument reply = await SaveActionAsync("""{ "id": "Rota Mal", "name": "Rota", "steps": [ { "type": "obs.marker", "marker": "short" } ] }""");

        JsonElement error = reply.RootElement.GetProperty("error");
        Assert.Equal(ErrorCodes.ConfigInvalid, error.GetProperty("code").GetString());
        Assert.StartsWith("actions[", error.GetProperty("where").GetString(), StringComparison.Ordinal);
        Assert.NotEqual(0, error.GetProperty("problems").GetArrayLength());
        Assert.Equal(before, ConfigText);
    }

    [Fact]
    public async Task A_key_is_assigned_and_then_cleared()
    {
        string before = ConfigText;

        using JsonDocument assigned = await AssignAsync("""{"page":"grabacion","position":1,"label":"Uno","shortLabel":["UNO"],"action":"mic","onHold":"marca-largo","hotkey":"Ctrl+Shift+Alt+F1"}""");
        Key key = _rig.Library.Current.Pages![0].Keys!.Single(k => k.Position == 1);
        using JsonDocument cleared = await AssignAsync("""{"page":"grabacion","position":1,"clear":true}""");

        Assert.True(Code(assigned) is null, assigned.RootElement.GetRawText());
        Assert.Equal(("Uno", "mic", "marca-largo", "Ctrl+Shift+Alt+F1"), (key.Label, key.Action, key.OnHold?.Action, key.Hotkey));
        Assert.Equal(["UNO"], key.ShortLabel!);
        Assert.Null(Code(cleared));
        Assert.DoesNotContain(_rig.Library.Current.Pages![0].Keys!, k => k.Position == 1);
        Assert.Equal(before, ConfigText);
    }

    [Fact]
    public async Task Reassigning_a_key_keeps_its_unknown_fields()
    {
        File.WriteAllText(File_, ConfigText.Replace("\"position\": 3,", "\"position\": 3, \"inventado\": \"en la tecla\",", StringComparison.Ordinal));
        _rig.Library.Load();

        using JsonDocument reply = await AssignAsync("""{"page":"grabacion","position":3,"label":"Banco","action":"mic"}""");

        Assert.Null(Code(reply));
        Key key = _rig.Library.Current.Pages![0].Keys!.Single(k => k.Position == 3);
        Assert.Equal("en la tecla", key.Extra["inventado"].GetString());
        Assert.Null(key.Hotkey);
    }

    [Fact]
    public async Task A_key_that_stops_with_a_tap_is_refused_and_writes_nothing()
    {
        string before = ConfigText;

        using JsonDocument reply = await AssignAsync("""{"page":"grabacion","position":1,"label":"Parar","action":"parar"}""");

        Assert.Equal(ErrorCodes.ConfigInvalid, Code(reply));
        Assert.Contains(reply.RootElement.GetProperty("error").GetProperty("problems").EnumerateArray(), p => p.GetProperty("code").GetString() == ProblemCodes.HoldRequired);
        Assert.Equal(before, ConfigText);
    }

    [Theory]
    [InlineData("""{"page":"no-esta","position":1,"label":"X","action":"mic"}""", ErrorCodes.ConfigInvalid, "page")]
    [InlineData("""{"page":"grabacion","position":13,"label":"X","action":"mic"}""", ErrorCodes.InvalidData, "position")]
    [InlineData("""{"page":"grabacion","position":0,"clear":true}""", ErrorCodes.InvalidData, "position")]
    [InlineData("""{"position":1,"label":"X"}""", ErrorCodes.InvalidData, "page")]
    [InlineData("""{"page":"grabacion","position":1,"shortLabel":[null]}""", ErrorCodes.InvalidData, "shortLabel[0]")]
    public async Task A_page_or_a_key_that_does_not_exist_is_refused_and_writes_nothing(string fields, string code, string where)
    {
        string before = ConfigText;

        using JsonDocument reply = await AssignAsync(fields);

        Assert.Equal(code, Code(reply));
        Assert.Equal(where, reply.RootElement.GetProperty("error").GetProperty("where").GetString());
        Assert.Equal(before, ConfigText);
    }

    [Theory]
    [InlineData("""{"expectedFingerprint":"x"}""", "action")]
    [InlineData("""{"action":{"name":"Sin id","steps":[]},"expectedFingerprint":"x"}""", "action.id")]
    [InlineData("""{"action":{"id":"a","steps":[null]},"expectedFingerprint":"x"}""", "action.steps[0]")]
    [InlineData("""{"action":{"id":"a","steps":[]}}""", "expectedFingerprint")]
    public async Task Action_save_requires_its_fields(string data, string where)
    {
        using JsonDocument reply = await AskAsync("action.save", data);

        Assert.Equal(ErrorCodes.InvalidData, Code(reply));
        Assert.Equal(where, reply.RootElement.GetProperty("error").GetProperty("where").GetString());
    }

    [Fact]
    public async Task With_a_take_in_progress_both_are_refused_and_write_nothing()
    {
        string before = ConfigText;
        _rig.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });

        using JsonDocument action = await SaveActionAsync(NewAction);
        using JsonDocument key = await AssignAsync("""{"page":"grabacion","position":1,"label":"Uno","action":"mic"}""");

        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, Code(action));
        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, Code(key));
        Assert.Equal(before, ConfigText);
    }

    [Theory]
    [InlineData("action.save")]
    [InlineData("key.assign")]
    public async Task From_the_window_both_are_no_permission(string request)
    {
        using JsonDocument reply = await AskAsync(request, $$"""{"expectedFingerprint":"{{_rig.Library.Fingerprint}}"}""", Caller.Window);

        Assert.Equal(ErrorCodes.NoPermission, Code(reply));
    }

    // La tubería cancela el token si el cliente se va (PipeListener): un guardado que empezó termina, y el archivo queda entero.
    [Fact]
    public async Task A_cancelled_token_does_not_leave_config_json_half_written()
    {
        string before = ConfigText;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using JsonDocument reply = await AskAsync(
            "action.save", $$"""{"action":{{NewAction}},"expectedFingerprint":"{{_rig.Library.Fingerprint}}"}""", ct: cts.Token);

        string after = ConfigText;
        Assert.True(after == before || _rig.Library.Current.FindAction("nueva") is not null, after);
        using JsonDocument parsed = JsonDocument.Parse(after);
        Assert.Null(Code(reply));
    }
}
