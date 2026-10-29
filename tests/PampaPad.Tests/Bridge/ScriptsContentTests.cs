using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using PampaPad.Core;
using IOFile = System.IO.File;

namespace PampaPad.Tests;

/// <summary>
/// El contenido de los scripts y crear uno (E-MCP-7): scripts.read con content, scripts.write con files y la huella de la carpeta
/// (ScriptFingerprint, decisión 2). Con scripts de mentira en una biblioteca temporal.
/// </summary>
public sealed class ScriptsContentTests : IAsyncLifetime
{
    private readonly ScriptRig _rig = new();
    private readonly StateStore _state = new();
    private readonly ScriptOutputSink _output = new();
    private readonly ConcurrentQueue<ScriptOutputEvent> _events = new();
    private readonly Dispatcher _dispatcher;
    private readonly ScriptRuns _runs;
    private readonly Bridge _bridge;
    private readonly IDisposable _hooks;

    public ScriptsContentTests()
    {
        var step = new ScriptRun(_rig.Runner, _rig.Library, _output);
        var executor = new Executor([step], [], TimeProvider.System);
        _dispatcher = new Dispatcher(_rig.Library, _state, executor, new Gestures(TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness), _rig.Log);
        _runs = new ScriptRuns(_rig.Log, Path.Combine(_rig.LibraryFolder, "no-hay-registro"), TimeProvider.System);
        _bridge = new Bridge(_state, _rig.Log) { McpAllowed = () => true };
        _hooks = Requests.RegisterAll(
            _bridge,
            new StatusSources("9.9.9", _state, _rig.Library, _dispatcher, _rig.Log, [step], [], () => new SettingsView(false, null, null)),
            new EditSources(_rig.Library, _state, _rig.Log, executor, [step], [], _dispatcher, new FakeWindows(), null));
        ScriptsRequests.Register(_bridge, new ScriptsSources(_rig.Library, _rig.Runner, _runs, new FakeShellDialogs(), Path.Combine(_rig.LibraryFolder, "..", "respaldo"), TimeProvider.System));
        _output.Published += _events.Enqueue;
    }

    private string Scripts => Path.Combine(_rig.LibraryFolder, "scripts");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _hooks.Dispose();
        _dispatcher.CancelAll();
        await Poll.Until(() => _rig.Runner.Running.Count == 0, 20_000);
        _runs.Dispose();
        await _state.DisposeAsync();
        await _rig.DisposeAsync();
    }

    [Fact]
    public async Task Read_with_content_brings_the_text_of_a_cmd_but_not_of_an_exe_nor_of_a_file_of_300_kb()
    {
        string folder = _rig.AddCmd("hola", "@echo off\necho canción\n");
        IOFile.WriteAllBytes(Path.Combine(folder, "otro.exe"), [0x4d, 0x5a]);
        IOFile.WriteAllText(Path.Combine(folder, "grande.ps1"), new string('x', 300 * 1024));

        JsonElement data = await OkAsync("scripts.read", """{"id":"hola","content":true}""");
        JsonElement plain = await OkAsync("scripts.read", """{"id":"hola"}""");

        JsonElement[] files = [.. data.GetProperty("files").EnumerateArray()];
        Assert.Equal("@echo off\r\necho canción\r\n", Entry(files, "hola.cmd").GetProperty("text").GetString());
        Assert.False(Entry(files, "otro.exe").TryGetProperty("text", out _));
        Assert.False(Entry(files, "grande.ps1").TryGetProperty("text", out _));
        Assert.Equal(300 * 1024, Entry(files, "grande.ps1").GetProperty("size").GetInt64());
        Assert.All(plain.GetProperty("files").EnumerateArray(), file => Assert.False(file.TryGetProperty("text", out _)));
        Assert.Equal(ScriptFingerprint.Of(folder), data.GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task A_new_script_is_created_with_its_cmd_and_its_manifest_and_then_tested()
    {
        JsonElement written = await OkAsync("scripts.write", Write("nuevo", "nuevo.cmd", """[{"name":"nuevo.cmd","text":"@echo off\r\necho listo\r\n"}]""", ""), Caller.Mcp);

        string folder = Path.Combine(Scripts, "nuevo");
        Assert.Equal(ScriptFingerprint.Of(folder), written.GetProperty("fingerprint").GetString());
        Assert.Equal(["nuevo.cmd", "script.json"], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        JsonElement test = await OkAsync("scripts.test", """{"id":"nuevo"}""", Caller.Mcp);
        Guid runId = test.GetProperty("runId").GetGuid();
        await Poll.Until(() => _events.Any(e => e.RunId == runId && e.End is not null), 20_000);
        Assert.Equal("listo", Assert.Single(_events, e => e.RunId == runId && e.End is null).Line);
    }

    [Fact]
    public async Task The_cmd_is_replaced_with_the_fingerprint_of_what_was_read()
    {
        _rig.AddCmd("hola", "@echo off\necho uno\n");
        string read = (await OkAsync("scripts.read", """{"id":"hola","content":true}""", Caller.Mcp)).GetProperty("fingerprint").GetString()!;

        JsonElement written = await OkAsync("scripts.write", Write("hola", "hola.cmd", """[{"name":"hola.cmd","text":"@echo off\r\necho dos\r\n"}]""", read), Caller.Mcp);

        string folder = Path.Combine(Scripts, "hola");
        Assert.Equal("@echo off\r\necho dos\r\n", IOFile.ReadAllText(Path.Combine(folder, "hola.cmd"), ScriptLaunch.Oem));
        Assert.NotEqual(read, written.GetProperty("fingerprint").GetString());
        Assert.Equal(ScriptFingerprint.Of(folder), written.GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task If_the_cmd_changed_on_disk_after_reading_nothing_is_written()
    {
        string folder = _rig.AddCmd("hola", "@echo off\necho uno\n");
        string read = (await OkAsync("scripts.read", """{"id":"hola","content":true}""", Caller.Mcp)).GetProperty("fingerprint").GetString()!;
        IOFile.WriteAllText(Path.Combine(folder, "hola.cmd"), "@echo off\r\necho a mano\r\n");
        Dictionary<string, byte[]> before = Snapshot(folder);

        string code = await FailAsync("scripts.write", Write("hola", "hola.cmd", """[{"name":"hola.cmd","text":"echo pisa"},{"name":"extra.py","text":"print(1)"}]""", read), Caller.Mcp);

        Assert.Equal(ErrorCodes.ConfigChangedOnDisk, code);
        Assert.Equal(before, Snapshot(folder));
    }

    [Theory]
    [InlineData("../x.cmd")]
    [InlineData("..\\\\x.cmd")]
    [InlineData("sub/x.cmd")]
    [InlineData("C:\\\\x.cmd")]
    [InlineData("x.exe")]
    [InlineData("x.txt")]
    [InlineData("script.json")]
    [InlineData("..")]
    public async Task A_name_that_is_not_a_text_file_of_the_folder_is_refused_and_nothing_is_written(string name)
    {
        string folder = _rig.AddCmd("hola", "@echo off\n");
        Dictionary<string, byte[]> before = Snapshot(folder);

        string code = await FailAsync("scripts.write", Write("hola", "hola.cmd", $$"""[{"name":"ok.cmd","text":"echo"},{"name":"{{name}}","text":"echo"}]""", ScriptFingerprint.Of(folder)), Caller.Mcp);

        Assert.Equal(ErrorCodes.InvalidData, code);
        Assert.Equal(before, Snapshot(folder));
        Assert.Equal(["hola"], Directory.GetFileSystemEntries(Scripts).Select(Path.GetFileName));
        Assert.False(IOFile.Exists(Path.Combine(_rig.LibraryFolder, "x.cmd")));
    }

    [Fact]
    public async Task A_file_of_300_kb_is_refused_and_a_new_folder_is_not_created()
    {
        string text = new('x', 300 * 1024);

        string code = await FailAsync("scripts.write", Write("grande", "grande.cmd", $$"""[{"name":"grande.cmd","text":"{{text}}"}]""", ""), Caller.Mcp);

        Assert.Equal(ErrorCodes.InvalidData, code);
        Assert.False(Directory.Exists(Path.Combine(Scripts, "grande")));
    }

    [Theory]
    [InlineData("Nuevo")]
    [InlineData("con espacio")]
    [InlineData("..")]
    public async Task An_id_with_capitals_or_spaces_is_refused(string id)
    {
        string code = await FailAsync("scripts.write", Write(id, "a.cmd", """[{"name":"a.cmd","text":"echo"}]""", ""), Caller.Mcp);

        Assert.Equal(ErrorCodes.InvalidData, code);
        Assert.Empty(Directory.GetFileSystemEntries(Scripts));
    }

    [Fact]
    public async Task Creating_needs_an_empty_fingerprint_and_an_entry_among_the_files()
    {
        string stale = await FailAsync("scripts.write", Write("nuevo", "nuevo.cmd", """[{"name":"nuevo.cmd","text":"echo"}]""", "vieja"), Caller.Mcp);
        string noEntry = await FailAsync("scripts.write", Write("nuevo", "otro.cmd", """[{"name":"nuevo.cmd","text":"echo"}]""", ""), Caller.Mcp);

        Assert.Equal(ErrorCodes.ConfigChangedOnDisk, stale);
        Assert.Equal(ErrorCodes.InvalidData, noEntry);
        Assert.Empty(Directory.GetFileSystemEntries(Scripts));
    }

    [Fact]
    public async Task The_manifest_from_the_window_without_files_still_works_with_the_new_fingerprint()
    {
        string folder = _rig.AddCmd("hola", "@echo off\n");
        string read = (await OkAsync("scripts.read", """{"id":"hola"}""")).GetProperty("fingerprint").GetString()!;
        string onlyManifest = Fingerprints.Of(IOFile.ReadAllBytes(Path.Combine(folder, "script.json")));

        JsonElement written = await OkAsync("scripts.write", $$"""{"id":"hola","manifest":{"entry":"hola.cmd","name":"Hola"},"expectedFingerprint":"{{read}}"}""");

        Assert.NotEqual(onlyManifest, read);
        Assert.Equal(ScriptFingerprint.Of(folder), written.GetProperty("fingerprint").GetString());
        Assert.Equal(ErrorCodes.ConfigChangedOnDisk, await FailAsync("scripts.write", $$"""{"id":"hola","manifest":{"entry":"hola.cmd"},"expectedFingerprint":"{{onlyManifest}}"}"""));
    }

    [Fact]
    public async Task With_a_take_in_progress_the_mcp_cannot_write_a_script()
    {
        _state.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });

        string code = await FailAsync("scripts.write", Write("nuevo", "nuevo.cmd", """[{"name":"nuevo.cmd","text":"echo"}]""", ""), Caller.Mcp);

        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, code);
        Assert.Empty(Directory.GetFileSystemEntries(Scripts));
    }

    // La tubería cancela el token si el cliente se va: los archivos y la ficha se escriben igual, todos o ninguno.
    [Fact]
    public async Task A_cancelled_token_does_not_leave_the_folder_half_written()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        string reply = await _bridge.HandleAsync(
            $$"""{"id":1,"request":"scripts.write","data":{{Write("nuevo", "a.cmd", """[{"name":"a.cmd","text":"echo a"},{"name":"b.ps1","text":"echo b"}]""", "")}}}""", Caller.Mcp, cts.Token);

        Assert.Contains("\"ok\":true", reply, StringComparison.Ordinal);
        Assert.Equal(["a.cmd", "b.ps1", "script.json"], Directory.GetFiles(Path.Combine(Scripts, "nuevo")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    // Revisión de la tanda 2 (must): un directorio con el nombre del tercer archivo hace fallar el disco a mitad de la escritura.
    [Fact]
    public async Task A_disk_failure_midway_puts_back_an_existing_script_and_removes_what_was_new()
    {
        string folder = _rig.AddCmd("hola", "@echo off\necho uno\n");
        Directory.CreateDirectory(Path.Combine(folder, "b.ps1"));
        Dictionary<string, byte[]> before = Snapshot(folder);

        string code = await FailAsync("scripts.write", Write("hola", "hola.cmd", """[{"name":"hola.cmd","text":"echo dos"},{"name":"c.py","text":"print(1)"},{"name":"b.ps1","text":"echo b"}]""", ScriptFingerprint.Of(folder)), Caller.Mcp);

        Assert.Equal(ErrorCodes.InternalError, code);
        Assert.Equal(before, Snapshot(folder));
        Assert.True(Directory.Exists(Path.Combine(folder, "b.ps1")));
    }

    [Fact]
    public async Task A_disk_failure_midway_while_creating_leaves_no_folder()
    {
        // Un nombre de más de 255 caracteres pasa los chequeos y el disco lo rechaza al escribir, después de a.cmd.
        string code = await FailAsync("scripts.write", Write("nuevo", "a.cmd", $$"""[{"name":"a.cmd","text":"echo a"},{"name":"{{new string('x', 300)}}.cmd","text":"echo b"}]""", ""), Caller.Mcp);

        Assert.Equal(ErrorCodes.InternalError, code);
        Assert.False(Directory.Exists(Path.Combine(Scripts, "nuevo")));
    }

    // Revisión de la tanda 2 (must): la ventana y el MCP guardan a la vez con la misma huella; uno pasa y el otro tiene que releer.
    [Fact]
    public async Task Two_writes_at_once_with_the_same_fingerprint_let_only_one_through()
    {
        string folder = _rig.AddCmd("hola", "@echo off\n");
        for (int round = 0; round < 20; round++)
        {
            // Textos nuevos en cada vuelta: si el que gana deja el disco como estaba, la huella no cambia y el otro también pasa.
            string read = ScriptFingerprint.Of(folder);
            using var start = new ManualResetEventSlim();
            Task<string>[] writes = [.. new[] { $"uno {round}", $"dos {round}" }.Select(text => Task.Run(async () =>
            {
                start.Wait();
                using JsonDocument reply = await AskAsync("scripts.write", Write("hola", "hola.cmd", $$"""[{"name":"hola.cmd","text":"echo {{text}}"}]""", read), Caller.Mcp);
                return reply.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetProperty("code").GetString()! : "ok";
            }))];
            start.Set();

            Assert.Equal([ErrorCodes.ConfigChangedOnDisk, "ok"], (await Task.WhenAll(writes)).Order(StringComparer.Ordinal));
        }
    }

    // Revisión de la tanda 2 (should): la forma del id se pide al crear. Una carpeta que ya está con otro nombre sigue guardando su ficha.
    [Fact]
    public async Task The_window_still_saves_the_manifest_of_a_folder_whose_name_is_not_an_id()
    {
        string folder = _rig.AddCmd("Mi Script", "@echo off\n");

        JsonElement written = await OkAsync("scripts.write", $$"""{"id":"Mi Script","manifest":{"entry":"Mi Script.cmd","name":"Mío"},"expectedFingerprint":"{{ScriptFingerprint.Of(folder)}}"}""");

        Assert.Equal(ScriptFingerprint.Of(folder), written.GetProperty("fingerprint").GetString());
    }

    // Revisión de la tanda 2 (should): cmd.exe relee el .cmd mientras corre, así que reemplazarlo en pleno vuelo correría otra cosa.
    [Fact]
    public async Task The_files_of_a_script_that_is_running_are_not_replaced()
    {
        string folder = _rig.AddPs1("lento", ScriptRig.LaunchesPing, """, "timeoutSec": 30""");
        using var cancel = new CancellationTokenSource();
        Task<Result<ScriptResult>> running = _rig.RunAsync("lento", cancel.Token);
        await ScriptRig.ChildAsync(folder);
        byte[] before = IOFile.ReadAllBytes(Path.Combine(folder, "lento.ps1"));

        string code = await FailAsync("scripts.write", Write("lento", "lento.ps1", """[{"name":"lento.ps1","text":"echo otro"}]""", ScriptFingerprint.Of(folder)), Caller.Mcp);
        await cancel.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(ErrorCodes.Cancelled, code);
        Assert.Equal(before, IOFile.ReadAllBytes(Path.Combine(folder, "lento.ps1")));
    }

    // Revisión de la tanda 2 (should): un .cmd va en la página OEM. Lo que no entra ahí se rechaza, en vez de quedar como "?" o como otra
    // letra; y va con \r\n, porque cmd.exe con \n solo se pierde en goto y call :etiqueta.
    [Fact]
    public async Task A_cmd_with_a_character_the_console_page_lacks_is_refused_and_one_with_lf_is_saved_with_crlf()
    {
        using JsonDocument refused = await AskAsync("scripts.write", Write("nuevo", "nuevo.cmd", """[{"name":"nuevo.cmd","text":"echo ✓ listo"}]""", ""), Caller.Mcp);
        Assert.Equal("files[0].text", refused.RootElement.GetProperty("error").GetProperty("where").GetString());
        Assert.False(Directory.Exists(Path.Combine(Scripts, "nuevo")));

        await OkAsync("scripts.write", Write("nuevo", "nuevo.cmd", """[{"name":"nuevo.cmd","text":"@echo off\ngoto fin\n:fin\n"}]""", ""), Caller.Mcp);

        Assert.Equal(ScriptLaunch.Oem.GetBytes("@echo off\r\ngoto fin\r\n:fin\r\n"), IOFile.ReadAllBytes(Path.Combine(Scripts, "nuevo", "nuevo.cmd")));
    }

    private static string Write(string id, string entry, string files, string fingerprint) =>
        $$"""{"id":"{{id}}","manifest":{"entry":"{{entry}}"},"expectedFingerprint":"{{fingerprint}}","files":{{files}}}""";

    private static JsonElement Entry(JsonElement[] files, string name) => files.Single(file => file.GetProperty("name").GetString() == name);

    private static Dictionary<string, byte[]> Snapshot(string folder) =>
        Directory.GetFiles(folder).ToDictionary(file => Path.GetFileName(file), IOFile.ReadAllBytes);

    private async Task<JsonDocument> AskAsync(string request, string data, Caller caller) =>
        JsonDocument.Parse(await _bridge.HandleAsync($$"""{"id":1,"request":"{{request}}","data":{{data}}}""", caller, CancellationToken.None));

    private async Task<JsonElement> OkAsync(string request, string data, Caller caller = Caller.Window)
    {
        using JsonDocument reply = await AskAsync(request, data, caller);
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        return reply.RootElement.GetProperty("data").Clone();
    }

    private async Task<string> FailAsync(string request, string data, Caller caller = Caller.Window)
    {
        using JsonDocument reply = await AskAsync(request, data, caller);
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean(), reply.RootElement.ToString());
        return reply.RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }
}
