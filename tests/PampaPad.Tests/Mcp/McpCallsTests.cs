using System.Text.Json;
using PampaPad.Core;
using PampaPad.Mcp;

namespace PampaPad.Tests;

/// <summary>Qué pide cada herramienta por la tubería y qué le devuelve a Claude (05 §7; plan de la etapa MCP, E-MCP-8).</summary>
public sealed class McpCallsTests
{
    private static string Data(JsonElement request) => request.GetProperty("data").GetRawText();

    [Fact]
    public async Task State_read_asks_the_snapshot_without_the_library_and_with_preflight_also_runs_the_check()
    {
        await using var rig = await McpRig.StartAsync();
        rig.App.Reply = request => [request.GetProperty("request").GetString() == RequestNames.AppSnapshot
            ? FakePampaPad.Ok(request, """{"version":"0.8.0","config":{"pages":[]},"fingerprint":"abc","state":{"seq":3},"stepTypes":[],"conditionTypes":[],"settings":{}}""")
            : FakePampaPad.Ok(request, """{"checks":[]}""")];

        var plain = McpRig.Parse(await rig.CallAsync("state_read"));
        var checkedToo = McpRig.Parse(await rig.CallAsync("state_read", new() { ["preflight"] = true }));

        var data = plain.GetProperty("data");
        Assert.Equal(3, data.GetProperty("state").GetProperty("seq").GetInt32());
        Assert.False(data.TryGetProperty("config", out _));
        Assert.False(data.TryGetProperty("stepTypes", out _));
        Assert.False(plain.TryGetProperty("preflight", out _));
        Assert.Equal("""{"checks":[]}""", checkedToo.GetProperty("preflight").GetProperty("data").GetRawText());
        Assert.Equal([RequestNames.AppSnapshot, RequestNames.AppSnapshot, RequestNames.PreflightRun],
            rig.App.Requests.Select(request => request.GetProperty("request").GetString()));
    }

    [Fact]
    public async Task Library_read_keeps_the_library_with_its_fingerprint_and_asks_the_options_of_one_list()
    {
        await using var rig = await McpRig.StartAsync();
        rig.App.Reply = request => [request.GetProperty("request").GetString() == RequestNames.AppSnapshot
            ? FakePampaPad.Ok(request, """{"version":"0.8.0","config":{"pages":[]},"fingerprint":"abc","state":{"seq":3},"stepTypes":[{"id":"wait.time"}],"conditionTypes":[],"settings":{}}""")
            : FakePampaPad.Ok(request, """{"items":[{"name":"Cámara"}]}""")];

        var library = McpRig.Parse(await rig.CallAsync("library_read", new() { ["options"] = "scenes" }));

        var data = library.GetProperty("data");
        Assert.Equal("abc", data.GetProperty("fingerprint").GetString());
        Assert.Equal("wait.time", data.GetProperty("stepTypes")[0].GetProperty("id").GetString());
        Assert.True(data.TryGetProperty("config", out _));
        Assert.True(data.TryGetProperty("conditionTypes", out _));
        Assert.False(data.TryGetProperty("state", out _));
        Assert.Equal("Cámara", library.GetProperty("options").GetProperty("data").GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal("""{"list":"scenes"}""", Data(rig.App.Single(RequestNames.OptionsList)));
    }

    [Fact]
    public async Task Scripts_read_lists_without_an_id_and_reads_the_content_with_one()
    {
        await using var rig = await McpRig.StartAsync();

        await rig.CallAsync("scripts_read");
        await rig.CallAsync("scripts_read", new() { ["id"] = "hola" });

        Assert.Equal("{}", Data(rig.App.Single(RequestNames.ScriptsList)));
        Assert.Equal("""{"id":"hola","content":true}""", Data(rig.App.Single(RequestNames.ScriptsRead)));
    }

    [Fact]
    public async Task Log_read_passes_its_filters()
    {
        await using var rig = await McpRig.StartAsync();
        var runId = Guid.NewGuid().ToString();

        await rig.CallAsync("log_read", new() { ["count"] = 20, ["level"] = "warning", ["requestedBy"] = "mcp", ["runId"] = runId, ["script"] = "hola" });

        Assert.Equal($$"""{"count":20,"level":"warning","requestedBy":"mcp","runId":"{{runId}}","script":"hola"}""", Data(rig.App.Single(RequestNames.LogRead)));
    }

    [Fact]
    public async Task The_changing_tools_send_their_request_with_the_fingerprint()
    {
        await using var rig = await McpRig.StartAsync();

        await rig.CallAsync("action_save", new() { ["action"] = McpRig.Parse("""{"id":"saludo","steps":[],"extra":1}"""), ["expectedFingerprint"] = "abc" });
        await rig.CallAsync("key_assign", new() { ["page"] = "main", ["position"] = 3, ["expectedFingerprint"] = "abc", ["label"] = "Hola", ["shortLabel"] = new[] { "HO", "LA" }, ["action"] = "saludo" });
        await rig.CallAsync("key_assign", new() { ["page"] = "main", ["position"] = 4, ["expectedFingerprint"] = "abc", ["clear"] = true });
        await rig.CallAsync("script_write", new()
        {
            ["id"] = "hola",
            ["manifest"] = McpRig.Parse("""{"entry":"hola.cmd"}"""),
            ["expectedFingerprint"] = "",
            ["files"] = new[] { new { name = "hola.cmd", text = "@echo hola" } },
        });

        Assert.Equal("""{"action":{"id":"saludo","steps":[],"extra":1},"expectedFingerprint":"abc"}""", Data(rig.App.Single(RequestNames.ActionSave)));
        var keys = rig.App.Requests.Where(request => request.GetProperty("request").GetString() == RequestNames.KeyAssign).Select(Data).ToArray();
        Assert.Equal("""{"page":"main","position":3,"expectedFingerprint":"abc","label":"Hola","shortLabel":["HO","LA"],"action":"saludo"}""", keys[0]);
        Assert.Equal("""{"page":"main","position":4,"expectedFingerprint":"abc","clear":true}""", keys[1]);
        Assert.Equal("""{"id":"hola","manifest":{"entry":"hola.cmd"},"expectedFingerprint":"","files":[{"name":"hola.cmd","text":"@echo hola"}]}""", Data(rig.App.Single(RequestNames.ScriptsWrite)));
    }

    [Fact]
    public async Task Action_run_asks_for_the_saved_action_and_returns_how_it_finished()
    {
        await using var rig = await McpRig.StartAsync();
        rig.App.Reply = request => [FakePampaPad.Ok(request, """{"ok":true,"steps":[{"outcome":"ok"}]}""")];

        var result = McpRig.Parse(await rig.CallAsync("action_run", new() { ["id"] = "saludo" }));

        Assert.Equal("""{"id":"saludo"}""", Data(rig.App.Single(RequestNames.ActionRun)));
        Assert.Equal("ok", result.GetProperty("data").GetProperty("steps")[0].GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Script_test_gathers_its_own_lines_in_order_until_the_end_even_those_that_arrive_before_the_answer()
    {
        await using var rig = await McpRig.StartAsync();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        string Line(Guid run, string line, string stream) => "{\"event\":\"script.output\",\"seq\":1,\"data\":{\"runId\":\"" + run + "\",\"line\":\"" + line + "\",\"stream\":\"" + stream + "\"}}";
        string End(Guid run, string end) => "{\"event\":\"script.output\",\"seq\":1,\"data\":{\"runId\":\"" + run + "\",\"end\":" + end + "}}";
        rig.App.Reply = request => request.GetProperty("request").GetString() switch
        {
            RequestNames.ScriptsRead => [FakePampaPad.Ok(request, """{"script":{"id":"hola","timeoutSec":5}}""")],
            _ =>
            [
                Line(mine, "uno", "stdout"),
                Line(other, "ajena", "stdout"),
                """{"event":"action.finished","seq":2,"data":{}}""",
                FakePampaPad.Ok(request, "{\"runId\":\"" + mine + "\"}"),
                Line(mine, "dos", "stderr"),
                End(other, """{"ok":true,"durationMs":1}"""),
                Line(mine, "tres", "stdout"),
                End(mine, """{"ok":true,"exitCode":0,"durationMs":40}"""),
            ],
        };

        var result = McpRig.Parse(await rig.CallAsync("script_test", new() { ["id"] = "hola" }));

        Assert.Equal("""{"id":"hola"}""", Data(rig.App.Single(RequestNames.ScriptsTest)));
        var data = result.GetProperty("data");
        Assert.Equal(mine.ToString(), data.GetProperty("runId").GetString());
        Assert.Equal(["uno", "dos", "tres"], data.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("line").GetString()));
        Assert.Equal("stderr", data.GetProperty("lines")[1].GetProperty("stream").GetString());
        Assert.Equal(0, data.GetProperty("end").GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task Without_the_program_open_every_tool_answers_app_not_running_as_data()
    {
        await using var rig = await McpRig.StartAsync(app: false);

        var reply = McpRig.Parse(await rig.CallAsync("state_read"));

        Assert.False(reply.GetProperty("ok").GetBoolean());
        Assert.Equal(ErrorCodes.AppNotRunning, reply.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("PampaPad no está abierto", reply.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task An_error_from_the_program_comes_back_with_its_code_and_message()
    {
        await using var rig = await McpRig.StartAsync();
        rig.App.Reply = request => [FakePampaPad.Error(request, ErrorCodes.ReadOnlyWhileRecording, "Grabando: sólo lectura")];

        var reply = McpRig.Parse(await rig.CallAsync("action_run", new() { ["id"] = "saludo" }));

        Assert.Equal(ErrorCodes.ReadOnlyWhileRecording, reply.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("Grabando: sólo lectura", reply.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_long_answer_is_cut_at_60000_characters_and_says_so()
    {
        await using var rig = await McpRig.StartAsync();
        rig.App.Reply = request => [FakePampaPad.Ok(request, "{\"entries\":\"" + new string('x', 70_000) + "\"}")];

        var text = await rig.CallAsync("log_read");

        Assert.StartsWith("""{"id":""", text, StringComparison.Ordinal);
        Assert.Equal(ToolOutput.MaxChars, text.IndexOf('\n', StringComparison.Ordinal));
        Assert.Contains("Recortado", text[ToolOutput.MaxChars..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_over_the_line_limit_is_refused_here_and_never_reaches_the_program()
    {
        await using var rig = await McpRig.StartAsync();

        var reply = McpRig.Parse(await rig.CallAsync("script_write", new()
        {
            ["id"] = "grande",
            ["manifest"] = McpRig.Parse("{}"),
            ["expectedFingerprint"] = "",
            ["files"] = new[] { new { name = "grande.cmd", text = new string('x', PipeProtocol.MaxLine) } },
        }));

        Assert.Equal(ErrorCodes.InvalidData, reply.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(rig.App.Requests);
    }

    [Fact]
    public async Task A_program_that_cuts_the_connection_gives_a_readable_error()
    {
        await using var rig = await McpRig.StartAsync();
        rig.App.Reply = _ => null;

        var reply = McpRig.Parse(await rig.CallAsync("scripts_read"));

        Assert.Equal(ErrorCodes.NoResponse, reply.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("cortó", reply.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_program_that_does_not_answer_in_time_gives_no_response()
    {
        await using var app = new FakePampaPad { Reply = _ => [] };

        var reply = McpRig.Parse(await new PipeClient(app.Name).CallAsync(RequestNames.LogRead, new { }, TimeSpan.FromMilliseconds(300), CancellationToken.None));

        Assert.Equal(ErrorCodes.NoResponse, reply.GetProperty("error").GetProperty("code").GetString());
        Assert.Single(app.Requests);
    }
}
