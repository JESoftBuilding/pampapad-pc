using System.ComponentModel;
using ModelContextProtocol.Server;
using PampaPad.Core;

namespace PampaPad.Mcp.Tools;

/// <summary>action_run (05 §7): action.run, que contesta cuando la acción termina.</summary>
public sealed class ActionRun(PipeClient pipe)
{
    private const string Text = """
        Corre una acción ya guardada en la biblioteca, como si se apretara su tecla, y contesta cuando termina: si anduvo, qué paso
        falló y con qué código, cuánto tardó y lo que devolvió cada paso (la última línea de un script, los datos de un obs.request).
        Espera hasta 70 s. Una acción que no existe da config_invalid; si la misma acción ya está corriendo, cancelled. Respeta la
        regla de navegación, como una tecla. Para probar un script suelto está script_test.
        Mientras se graba, se está en pausa o se transmite, devuelve read_only_while_recording: grabando, Claude sólo lee.
        """;

    // Los 60 s de tope de una acción más 10 de margen (plan de la etapa MCP, "Lo que puede morder").
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(70);

    [McpServerTool(Name = "action_run")]
    [McpMeta(McpTools.RequiresUserInteraction, true)]
    [Description(Text)]
    public async Task<string> RunAsync(
        [Description("El id de la acción guardada.")] string id,
        CancellationToken cancellationToken = default) =>
        ToolOutput.Clip(await pipe.CallAsync(RequestNames.ActionRun, new { id }, Wait, cancellationToken).ConfigureAwait(false));
}
