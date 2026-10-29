using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PampaPad.Core;
using PampaPad.Mcp;

namespace PampaPad.Tests;

/// <summary>La lista fija de herramientas de PampaPad.Mcp (05 §7; plan de la etapa MCP, E-MCP-8), vista como la ve Claude Code.</summary>
public sealed class McpToolsTests
{
    private const string Interaction = "anthropic/requiresUserInteraction";

    private static readonly string[] Reading = ["state_read", "library_read", "scripts_read", "log_read"];
    private static readonly string[] ChangingOrRunning = ["action_save", "key_assign", "script_write", "action_run", "script_test"];

    [Fact]
    public async Task Nine_tools_are_listed_and_only_the_five_that_change_or_run_ask_for_the_user()
    {
        await using var rig = await McpRig.StartAsync();

        var tools = await rig.Client.ListToolsAsync();

        Assert.Equal(Reading.Concat(ChangingOrRunning).Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        foreach (var tool in tools)
        {
            var meta = tool.ProtocolTool.Meta;
            if (ChangingOrRunning.Contains(tool.Name))
            {
                Assert.True(meta?[Interaction]?.GetValue<bool>(), $"{tool.Name} no pide la aprobación");
            }
            else
            {
                Assert.True(meta is null || !meta.ContainsKey(Interaction), $"{tool.Name} lee y no debería pedir aprobación");
            }
        }
    }

    [Fact]
    public async Task Every_description_is_under_the_2048_characters_claude_code_keeps()
    {
        await using var rig = await McpRig.StartAsync();

        var tools = await rig.Client.ListToolsAsync();

        Assert.All(tools, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name);
            Assert.True(tool.Description.Length < 2048, $"{tool.Name}: {tool.Description.Length}");
        });
        Assert.All(tools.Where(tool => ChangingOrRunning.Contains(tool.Name)), tool => Assert.Contains("grab", tool.Description, StringComparison.Ordinal));
        Assert.All(tools.Where(tool => tool.Name is "action_save" or "key_assign" or "script_write"), tool => Assert.Contains("huella", tool.Description, StringComparison.Ordinal));
    }

    [Fact]
    public void The_smoke_test_list_is_the_nine_names()
    {
        Assert.Equal([.. Reading, .. ChangingOrRunning], McpTools.Names);
    }

    [Fact]
    public void The_pipe_is_named_after_the_copy_like_the_single_instance()
    {
        Assert.Equal("PampaPad-dev", PipeProtocol.Name(dev: true));
        Assert.Equal("PampaPad", PipeProtocol.Name(dev: false));
    }

    // El ejecutable de verdad por stdio: lo único que sale por la salida estándar es JSON-RPC, y arranca sin conectarse a la tubería.
    [Fact]
    public async Task The_executable_writes_only_protocol_on_standard_output_and_starts_fast()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "PampaPad.Mcp.exe");
        var start = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        var clock = Stopwatch.StartNew();
        using var process = Process.Start(start)!;
        process.StandardInput.AutoFlush = true;
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"prueba","version":"1"}}}""");
        var initialized = await process.StandardOutput.ReadLineAsync(timeout.Token);
        var startup = clock.Elapsed;
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var listed = await process.StandardOutput.ReadLineAsync(timeout.Token);
        process.StandardInput.Close();
        var rest = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal(1, McpRig.Parse(initialized!).GetProperty("id").GetInt32());
        var tools = McpRig.Parse(listed!).GetProperty("result").GetProperty("tools");
        Assert.Equal(9, tools.GetArrayLength());
        Assert.True(string.IsNullOrWhiteSpace(rest), "Salió algo más por la salida estándar: " + rest + await stderr);
        Assert.True(startup < TimeSpan.FromSeconds(5), $"Tardó {startup.TotalSeconds:0.0} s en contestar el initialize");
        Assert.Equal(JsonValueKind.Object, tools[0].GetProperty("inputSchema").ValueKind);
    }
}
