using ModelContextProtocol.Server;
using PampaPad.Mcp;

// PampaPad.Mcp (05 §7): habla MCP con Claude Code por stdio y, en cada llamada, abre la tubería del programa. Al arrancar no se conecta a
// nada, así contesta el initialize dentro de los 5 s de Claude Code. Por la salida estándar va sólo protocolo: no hay registro del host;
// lo que el runtime tenga que decir va a la salida de error.
if (args.Contains("--smoke-test", StringComparer.Ordinal))
{
    // La prueba de humo de publish.ps1: arma las herramientas como al correr y lista sus nombres, uno por línea.
    foreach (var name in McpTools.Names)
    {
        Console.Out.WriteLine(name);
    }

    return 0;
}

await using var server = McpServer.Create(new StdioServerTransport("pampapad"), McpHost.Options(new PipeClient(McpHost.PipeName)));
await server.RunAsync();
return 0;
