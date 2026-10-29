using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using PampaPad.Core;

namespace PampaPad.Mcp.Tools;

/// <summary>action_save (05 §7): action.save, una acción entera con la huella de config.json.</summary>
public sealed class ActionSave(PipeClient pipe)
{
    private const string Text = """
        Crea o reemplaza una acción de la biblioteca por su id: si ya hay una con ese id la reemplaza en su lugar; si no, la agrega al
        final. Va la acción entera, con la forma que da library_read (id, name, steps y lo demás); los campos que PampaPad no conoce se
        conservan. PampaPad valida antes de escribir: si algo está mal no escribe nada y devuelve config_invalid con cada problema y su
        ubicación. Bien guardada, rige en el acto y contesta la huella nueva, que es la que se usa en el próximo guardado.
        expectedFingerprint es la huella que dio library_read (o el guardado anterior). Si config.json cambió desde entonces, falla con
        config_changed_on_disk sin escribir: hay que volver a leer con library_read y rehacer el cambio sobre lo nuevo.
        Mientras se graba, se está en pausa o se transmite, devuelve read_only_while_recording: grabando, Claude sólo lee.
        """;

    [McpServerTool(Name = "action_save")]
    [McpMeta(McpTools.RequiresUserInteraction, true)]
    [Description(Text)]
    public async Task<string> SaveAsync(
        [Description("La acción entera, como un objeto JSON de config.json (actions[]).")] JsonElement action,
        [Description("La huella de config.json que se leyó con library_read.")] string expectedFingerprint,
        CancellationToken cancellationToken = default) =>
        ToolOutput.Clip(await pipe.CallAsync(
            RequestNames.ActionSave, new { action, expectedFingerprint }, PipeClient.RequestTimeout, cancellationToken).ConfigureAwait(false));
}
