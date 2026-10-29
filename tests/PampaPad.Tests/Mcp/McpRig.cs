using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PampaPad.Mcp;

namespace PampaPad.Tests;

/// <summary>
/// PampaPad.Mcp entero en memoria: el servidor MCP con sus herramientas, un cliente MCP como el de Claude Code y, del otro lado de la
/// tubería, el FakePampaPad. Habla JSON-RPC de verdad, así las pruebas ven lo mismo que Claude Code: nombres, _meta y argumentos.
/// </summary>
public sealed class McpRig : IAsyncDisposable
{
    private readonly CancellationTokenSource _life = new();
    private readonly McpServer _server;
    private readonly Task _running;

    // Los inicializadores corren antes del cuerpo: App ya tiene su nombre acá.
    private McpRig(bool app)
    {
        var pipeName = app ? App.Name : "PampaPad-test-" + Guid.NewGuid().ToString("N");
        var toServer = new Pipe();
        var toClient = new Pipe();
        _server = McpServer.Create(
            new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream(), "pampapad"),
            McpHost.Options(new PipeClient(pipeName)));
        _running = _server.RunAsync(_life.Token);
        ClientTransport = new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream());
    }

    public FakePampaPad App { get; } = new();

    public McpClient Client { get; private set; } = null!;

    private StreamClientTransport ClientTransport { get; }

    /// <summary>Con el FakePampaPad escuchando; con app: false, nadie atiende la tubería.</summary>
    public static async Task<McpRig> StartAsync(bool app = true)
    {
        var rig = new McpRig(app);
        rig.Client = await McpClient.CreateAsync(rig.ClientTransport);
        return rig;
    }

    /// <summary>Llama a la herramienta y devuelve su texto, que es lo que lee Claude.</summary>
    public async Task<string> CallAsync(string tool, Dictionary<string, object?>? arguments = null)
    {
        CallToolResult result = await Client.CallToolAsync(tool, arguments ?? []);
        Assert.False(result.IsError ?? false, string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text)));
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }

    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _life.CancelAsync();
        try
        {
            await _running;
        }
        catch (OperationCanceledException)
        {
        }

        await _server.DisposeAsync();
        await App.DisposeAsync();
        _life.Dispose();
    }
}
