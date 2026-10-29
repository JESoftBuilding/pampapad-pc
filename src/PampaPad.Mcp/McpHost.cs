using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PampaPad.Core;

namespace PampaPad.Mcp;

/// <summary>El servidor MCP de PampaPad: nombre, versión, la tubería de la copia y las herramientas.</summary>
public static class McpHost
{
    /// <summary>La tubería de la copia, elegida como AppIdentity: Debug habla con PampaPad-dev, Release con PampaPad (decisión 7).</summary>
#if DEBUG
    public static string PipeName { get; } = PipeProtocol.Name(dev: true);
#else
    public static string PipeName { get; } = PipeProtocol.Name(dev: false);
#endif

    public static string Version { get; } = typeof(McpHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static McpServerOptions Options(PipeClient pipe)
    {
        var options = new McpServerOptions { ServerInfo = new Implementation { Name = "pampapad", Version = Version } };
        options.ToolCollection = [];
        foreach (var tool in McpTools.Create(pipe))
        {
            options.ToolCollection.Add(tool);
        }

        return options;
    }
}
