using System.ComponentModel;
using ModelContextProtocol.Server;
using PampaPad.Core;

namespace PampaPad.Mcp.Tools;

/// <summary>log_read (05 §7): log.read con sus filtros.</summary>
public sealed class LogRead(PipeClient pipe)
{
    private const string Text = """
        Lee las últimas entradas del registro de PampaPad, de las 500 que guarda en memoria, la más nueva al final. Sirve para saber cómo
        terminó algo: una tecla, una acción, la prueba de un script o un pedido de este MCP (cada llamada queda con requestedBy mcp).
        Se filtra por nivel mínimo, por quién la disparó, por runId o por script. El registro nunca lleva secretos.
        Anda siempre, también grabando.
        """;

    [McpServerTool(Name = "log_read", ReadOnly = true)]
    [Description(Text)]
    public async Task<string> ReadAsync(
        [Description("Cuántas entradas, contando desde la más nueva.")] int? count = null,
        [Description("El nivel mínimo: info, warning o error.")] string? level = null,
        [Description("Quién la disparó: key, hotkey, window, mcp, tray o internal.")] string? requestedBy = null,
        [Description("El runId de una corrida, como lo devuelven action_run y script_test.")] string? runId = null,
        [Description("El id de un script.")] string? script = null,
        CancellationToken cancellationToken = default) =>
        ToolOutput.Clip(await pipe.CallAsync(
            RequestNames.LogRead, new { count, level, requestedBy, runId, script }, PipeClient.RequestTimeout, cancellationToken).ConfigureAwait(false));
}
