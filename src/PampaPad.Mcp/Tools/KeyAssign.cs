using System.ComponentModel;
using ModelContextProtocol.Server;
using PampaPad.Core;

namespace PampaPad.Mcp.Tools;

/// <summary>key_assign (05 §7): key.assign, una tecla de una página con la huella de config.json.</summary>
public sealed class KeyAssign(PipeClient pipe)
{
    private const string Text = """
        Asigna o vacía una tecla de una página de la biblioteca. La tecla queda exactamente con los campos que se mandan: los que no
        vienen quedan vacíos. Con clear en true la tecla se vacía y los demás campos no cuentan. action y onHold son ids de acciones que
        ya existen (action_save las crea). PampaPad valida antes de escribir: rechaza una tecla que detenga o pause la grabación con un
        toque (eso va en onHold) y un atajo repetido, y devuelve config_invalid con cada problema y su ubicación, sin escribir nada.
        Bien guardada, rige en el acto y contesta la huella nueva.
        expectedFingerprint es la huella que dio library_read (o el guardado anterior). Si config.json cambió desde entonces, falla con
        config_changed_on_disk sin escribir: hay que volver a leer con library_read.
        Mientras se graba, se está en pausa o se transmite, devuelve read_only_while_recording: grabando, Claude sólo lee.
        """;

    [McpServerTool(Name = "key_assign")]
    [McpMeta(McpTools.RequiresUserInteraction, true)]
    [Description(Text)]
    public async Task<string> AssignAsync(
        [Description("El id de la página.")] string page,
        [Description("La tecla, de 1 a 12.")] int position,
        [Description("La huella de config.json que se leyó con library_read.")] string expectedFingerprint,
        [Description("La etiqueta de la tecla.")] string? label = null,
        [Description("La etiqueta corta, renglón por renglón, para la pantalla de la consola.")] string[]? shortLabel = null,
        [Description("El id de la acción que corre con un toque.")] string? action = null,
        [Description("El id de la acción que corre al mantenerla.")] string? onHold = null,
        [Description("El atajo de teclado de la PC, como Ctrl+Alt+F1.")] string? hotkey = null,
        [Description("true para vaciar la tecla.")] bool clear = false,
        CancellationToken cancellationToken = default) =>
        ToolOutput.Clip(await pipe.CallAsync(
            RequestNames.KeyAssign,
            new { page, position, expectedFingerprint, label, shortLabel, action, onHold, hotkey, clear = clear ? true : (bool?)null },
            PipeClient.RequestTimeout,
            cancellationToken).ConfigureAwait(false));
}
