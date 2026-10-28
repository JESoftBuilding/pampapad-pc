using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>scripts.write, scripts.import, scripts.delete, scripts.reveal y library.reveal, con scripts de mentira (plan de la etapa 8, E8-4).</summary>
public sealed class ScriptsEditTests : IAsyncLifetime
{
    private readonly ScriptRig _rig;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 27, 15, 30, 0, TimeSpan.Zero));
    private readonly StateStore _state = new();
    private readonly FakeShellDialogs _shell = new();
    private readonly TempFolder _backup = new();
    private readonly ScriptRuns _runs;
    private readonly Bridge _bridge;

    public ScriptsEditTests()
    {
        _rig = new ScriptRig(_clock);
        _runs = new ScriptRuns(_rig.Log, Path.Combine(_rig.LibraryFolder, "no-hay-registro"), TimeProvider.System);
        _bridge = new Bridge(_state, _rig.Log);
        ScriptsRequests.Register(_bridge, new ScriptsSources(_rig.Library, _rig.Runner, _runs, _shell, _backup.Root, _clock));
    }

    private string Scripts => Path.Combine(_rig.LibraryFolder, "scripts");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _runs.Dispose();
        await _state.DisposeAsync();
        await _rig.DisposeAsync();
        _backup.Dispose();
    }

    [Fact]
    public async Task Write_keeps_an_unknown_field_and_answers_the_new_fingerprint()
    {
        string folder = _rig.AddCmd("hola", "@echo off\n", """, "inventado": { "a": 1 }""");
        string before = Fingerprint(folder);

        JsonElement data = await OkAsync("scripts.write", $$"""{"id":"hola","manifest":{"entry":"hola.cmd","name":"Hola","timeoutSec":59},"expectedFingerprint":"{{before}}"}""");

        using JsonDocument written = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "script.json")));
        Assert.Equal("Hola", written.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, written.RootElement.GetProperty("inventado").GetProperty("a").GetInt32());
        Assert.Equal(Fingerprint(folder), data.GetProperty("fingerprint").GetString());
        Assert.NotEqual(before, Fingerprint(folder));
    }

    [Fact]
    public async Task Write_with_an_old_fingerprint_does_not_write()
    {
        string folder = _rig.AddCmd("hola", "@echo off\n");
        string before = File.ReadAllText(Path.Combine(folder, "script.json"));

        string code = await FailAsync("scripts.write", """{"id":"hola","manifest":{"entry":"hola.cmd","name":"Otro"},"expectedFingerprint":"vieja"}""");

        Assert.Equal(ErrorCodes.ConfigChangedOnDisk, code);
        Assert.Equal(before, File.ReadAllText(Path.Combine(folder, "script.json")));
    }

    [Theory]
    [InlineData("""{"entry":"noesta.cmd"}""", "manifest.entry")]
    [InlineData("""{"entry":"hola.cmd","timeoutSec":0}""", "manifest.timeoutSec")]
    [InlineData("""{"entry":"hola.cmd","timeoutSec":60}""", "manifest.timeoutSec")]
    public async Task Write_refuses_an_entry_that_is_not_there_and_a_limit_out_of_1_to_59(string manifest, string where)
    {
        string folder = _rig.AddCmd("hola", "@echo off\n");

        using JsonDocument reply = await AskAsync("scripts.write", $$"""{"id":"hola","manifest":{{manifest}},"expectedFingerprint":"{{Fingerprint(folder)}}"}""");

        JsonElement error = reply.RootElement.GetProperty("error");
        Assert.Equal(ErrorCodes.InvalidData, error.GetProperty("code").GetString());
        Assert.Equal(where, error.GetProperty("where").GetString());
    }

    [Fact]
    public async Task Import_builds_the_folder_and_the_id_and_adds_2_when_it_exists()
    {
        using var source = new TempFolder();
        string cmd = source.Combine("Banco Cam.cmd");
        string ps1 = source.Combine("Banco Cam.ps1");
        File.WriteAllText(cmd, "@echo off");
        File.WriteAllText(ps1, "Write-Output 'x'");
        _shell.Picks.Enqueue([cmd, ps1]);
        _shell.Picks.Enqueue([cmd]);

        string first = (await OkAsync("scripts.import", "{}")).GetProperty("id").GetString()!;
        string second = (await OkAsync("scripts.import", "{}")).GetProperty("id").GetString()!;

        Assert.Equal("banco-cam", first);
        Assert.Equal("banco-cam-2", second);
        Assert.True(File.Exists(Path.Combine(Scripts, "banco-cam", "Banco Cam.ps1")));
        Result<ScriptInfo> read = ScriptFolder.Read(_rig.LibraryFolder, "banco-cam");
        Assert.True(read.Ok, read.Failure?.Message);
        Assert.Equal("Banco Cam.cmd", read.Value!.Manifest.Entry);
        Assert.Equal("Banco Cam", read.Value.Manifest.Name);
        Assert.Equal(1, read.Value.Manifest.Contract);
    }

    [Fact]
    public async Task Cancelling_the_import_dialog_answers_an_empty_id_and_creates_nothing()
    {
        JsonElement data = await OkAsync("scripts.import", "{}");

        Assert.Equal("", data.GetProperty("id").GetString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(Scripts));
    }

    [Fact]
    public async Task Delete_refuses_a_script_in_use_and_says_where()
    {
        string folder = _rig.AddCmd("bancocam", "@echo off\n");
        File.WriteAllText(Path.Combine(_rig.LibraryFolder, "config.json"), """
            { "version": 1, "actions": [ { "id": "ir-al-banco", "name": "Ir al banco", "steps": [ { "type": "script.run", "script": "bancocam" } ] } ] }
            """);
        _rig.Library.Load();

        using JsonDocument reply = await AskAsync("scripts.delete", """{"id":"bancocam"}""");

        JsonElement error = reply.RootElement.GetProperty("error");
        Assert.Equal(ErrorCodes.ConfigInvalid, error.GetProperty("code").GetString());
        Assert.Equal("actions[0].steps[0].script", error.GetProperty("problems")[0].GetProperty("where").GetString());
        Assert.Contains("Ir al banco", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task Delete_refuses_a_script_that_is_running()
    {
        string folder = _rig.AddPs1("lento", ScriptRig.LaunchesPing, """, "timeoutSec": 30""");
        using var cancel = new CancellationTokenSource();
        Task<Result<ScriptResult>> running = _rig.RunAsync("lento", cancel.Token);
        await ScriptRig.ChildAsync(folder);

        string code = await FailAsync("scripts.delete", """{"id":"lento"}""");
        await cancel.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(ErrorCodes.Cancelled, code);
        Assert.True(Directory.Exists(folder));
    }

    // El disco no distingue mayúsculas: "Hola" encuentra la carpeta hola, y lo que corre como hola tiene que verse igual (E-MCP-4). En
    // scripts.read, "Hola" sale con status problem (id_invalid va antes que running): por eso se mira Running.
    [Fact]
    public async Task A_script_running_as_hola_is_seen_running_from_Hola_and_removing_it_is_refused()
    {
        string folder = _rig.AddPs1("hola", ScriptRig.LaunchesPing, """, "timeoutSec": 30""");
        using var cancel = new CancellationTokenSource();
        Task<Result<ScriptResult>> running = _rig.RunAsync("hola", cancel.Token);
        await ScriptRig.ChildAsync(folder);

        bool seen = _rig.Runner.Running.Contains("Hola");
        string code = await FailAsync("scripts.delete", """{"id":"Hola"}""");
        await cancel.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(seen);
        Assert.Equal(ErrorCodes.Cancelled, code);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task A_script_in_use_as_bancocam_is_not_removed_as_Bancocam()
    {
        string folder = _rig.AddCmd("bancocam", "@echo off\n");
        File.WriteAllText(Path.Combine(_rig.LibraryFolder, "config.json"), """
            { "version": 1, "actions": [ { "id": "ir-al-banco", "name": "Ir al banco", "steps": [ { "type": "script.run", "script": "bancocam" } ] } ] }
            """);
        _rig.Library.Load();

        Assert.Equal(ErrorCodes.ConfigInvalid, await FailAsync("scripts.delete", """{"id":"Bancocam"}"""));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task Write_with_a_null_tag_is_invalid_data_and_the_manifest_stays()
    {
        string folder = _rig.AddCmd("hola", "@echo off\n");
        byte[] before = File.ReadAllBytes(Path.Combine(folder, "script.json"));

        using JsonDocument reply = await AskAsync("scripts.write", $$"""{"id":"hola","manifest":{"entry":"hola.cmd","tags":[null]},"expectedFingerprint":"{{Fingerprint(folder)}}"}""");

        Assert.Equal(ErrorCodes.InvalidData, reply.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("manifest.tags[0]", reply.RootElement.GetProperty("error").GetProperty("where").GetString());
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(folder, "script.json")));
    }

    [Fact]
    public async Task Delete_moves_the_folder_to_the_backup()
    {
        _rig.AddCmd("hola", "@echo off\n");

        string moved = (await OkAsync("scripts.delete", """{"id":"hola"}""")).GetProperty("backupPath").GetString()!;

        Assert.False(Directory.Exists(Path.Combine(Scripts, "hola")));
        Assert.Equal(Path.Combine(_backup.Root, "scripts", "hola-20261027-153000"), moved, ignoreCase: true);
        Assert.True(File.Exists(Path.Combine(moved, "hola.cmd")));
        Assert.True(File.Exists(Path.Combine(moved, "script.json")));
    }

    [Fact]
    public async Task If_the_backup_cannot_be_written_the_original_stays()
    {
        string folder = _rig.AddCmd("hola", "@echo off\n");
        File.WriteAllText(Path.Combine(_backup.Root, "scripts"), "un archivo donde va la carpeta del respaldo");

        await FailAsync("scripts.delete", """{"id":"hola"}""");

        Assert.True(File.Exists(Path.Combine(folder, "hola.cmd")));
        Assert.True(File.Exists(Path.Combine(folder, "script.json")));
    }

    [Fact]
    public async Task Reveal_asks_for_the_right_folder_with_and_without_id()
    {
        string folder = _rig.AddCmd("hola", "@echo off\n");

        await OkAsync("scripts.reveal", """{"id":"hola"}""");
        await OkAsync("scripts.reveal", "{}");
        await OkAsync("library.reveal", "{}");
        string missing = await FailAsync("scripts.reveal", """{"id":"noesta"}""");

        Assert.Equal([folder, Scripts, _rig.LibraryFolder], _shell.Revealed);
        Assert.Equal(ErrorCodes.ScriptNotFound, missing);
    }

    private static string Fingerprint(string folder) => Fingerprints.Of(File.ReadAllBytes(Path.Combine(folder, "script.json")));

    private async Task<JsonDocument> AskAsync(string request, string data) =>
        JsonDocument.Parse(await _bridge.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", Caller.Window, CancellationToken.None));

    private async Task<JsonElement> OkAsync(string request, string data)
    {
        using JsonDocument reply = await AskAsync(request, data);
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        return reply.RootElement.GetProperty("data").Clone();
    }

    private async Task<string> FailAsync(string request, string data)
    {
        using JsonDocument reply = await AskAsync(request, data);
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        return reply.RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }
}
