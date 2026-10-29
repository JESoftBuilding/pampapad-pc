using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using PampaPad.Core;

namespace PampaPad.Mcp.Tools;

/// <summary>library_read (05 §7): la parte de app.snapshot que es la biblioteca y, si se pide, una lista de options.list.</summary>
public sealed class LibraryRead(PipeClient pipe)
{
    private const string Text = """
        Lee la biblioteca de PampaPad: config.json entero (páginas, teclas, acciones con sus pasos, atajos) con su huella en
        "fingerprint", y los tipos de paso y de condición con sus campos. La huella es la que piden action_save y key_assign: si
        config.json cambió desde que se leyó (lo guardó la ventana, Emmanuel lo editó a mano u otra llamada), esos guardados fallan con
        config_changed_on_disk y hay que volver a leer con esta herramienta antes de reintentar.
        Con options pide además una lista para elegir en un campo y la devuelve en "options": scenes y inputs (de OBS, con su
        identificador; con OBS cerrado dice por qué no hay), scripts o windows. Anda siempre, también grabando.
        """;

    // Lo que no es de la biblioteca: lo da state_read.
    private static readonly string[] Library = ["config", "fingerprint", "stepTypes", "conditionTypes"];

    [McpServerTool(Name = "library_read", ReadOnly = true)]
    [Description(Text)]
    public Task<string> ReadAsync(
        [Description("Una lista de opciones para traer también: scenes, inputs, scripts o windows.")] string? options = null,
        CancellationToken cancellationToken = default) =>
        pipe.RunAsync(PipeClient.RequestTimeout, async (session, ct) =>
        {
            JsonObject library = ToolOutput.Trim(
                await session.SendAsync(RequestNames.AppSnapshot, new { }, null, ct).ConfigureAwait(false), Library.Contains);
            if (options is not null && ToolOutput.IsOk(library))
            {
                library["options"] = JsonNode.Parse(
                    await session.SendAsync(RequestNames.OptionsList, new { list = options }, null, ct).ConfigureAwait(false));
            }

            return ToolOutput.Clip(library);
        }, cancellationToken);
}
