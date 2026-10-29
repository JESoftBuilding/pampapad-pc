using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using PampaPad.Core;

namespace PampaPad.Mcp.Tools;

/// <summary>state_read (05 §7): app.snapshot sin la biblioteca y, si se pide, el chequeo previo con preflight.run.</summary>
public sealed class StateRead(PipeClient pipe)
{
    private const string Text = """
        Lee el estado de PampaPad: OBS (conexión, grabación, escena al aire, micrófono), la consola, la página que rige, las acciones en
        curso, los problemas de la configuración, los ajustes de esta PC y la versión. No trae la biblioteca: para eso está library_read.
        Con preflight en true corre además el chequeo previo y lo devuelve en "preflight", comprobación por comprobación.
        Anda siempre, también mientras se graba. Así se sabe si se está grabando antes de cambiar o ejecutar algo: grabando, en pausa o
        transmitiendo, PampaPad sólo deja leer. Si el programa está cerrado devuelve app_not_running; si el MCP está apagado en Ajustes,
        no_permission.
        """;

    // Lo que es de la biblioteca: lo da library_read.
    private static readonly string[] Library = ["config", "fingerprint", "stepTypes", "conditionTypes"];

    [McpServerTool(Name = "state_read", ReadOnly = true)]
    [Description(Text)]
    public Task<string> ReadAsync(
        [Description("true para correr también el chequeo previo.")] bool preflight = false,
        CancellationToken cancellationToken = default) =>
        pipe.RunAsync(PipeClient.RequestTimeout, async (session, ct) =>
        {
            JsonObject state = ToolOutput.Trim(
                await session.SendAsync(RequestNames.AppSnapshot, new { }, null, ct).ConfigureAwait(false), key => !Library.Contains(key));
            if (preflight && ToolOutput.IsOk(state))
            {
                state["preflight"] = JsonNode.Parse(await session.SendAsync(RequestNames.PreflightRun, new { }, null, ct).ConfigureAwait(false));
            }

            return ToolOutput.Clip(state);
        }, cancellationToken);
}
