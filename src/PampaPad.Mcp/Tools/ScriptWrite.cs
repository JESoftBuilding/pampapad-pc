using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using PampaPad.Core;

namespace PampaPad.Mcp.Tools;

/// <summary>script_write (05 §7): scripts.write con la ficha y los archivos de texto.</summary>
public sealed class ScriptWrite(PipeClient pipe)
{
    private const string Text = """
        Crea o reemplaza un script de la biblioteca, en scripts/<id>/: su ficha (script.json) y, si vienen, sus archivos de texto. Sólo
        .cmd, .ps1 y .py, de hasta 256 KB cada uno, con el nombre del archivo solo (sin carpetas). El entry de la ficha tiene que ser uno de
        los archivos de la carpeta. Para crear uno nuevo, expectedFingerprint va vacío; el id, en minúsculas y sin espacios.
        Para reemplazar, expectedFingerprint es la huella que dio scripts_read con ese id: cubre la ficha y los archivos de texto. Si algo
        cambió desde entonces (por ejemplo, Emmanuel editó el .cmd a mano), falla con config_changed_on_disk sin escribir nada y hay que
        volver a leer con scripts_read. Un archivo que no viene no se toca. Contesta la huella nueva.
        Mientras se graba, se está en pausa o se transmite, devuelve read_only_while_recording: grabando, Claude sólo lee.
        """;

    [McpServerTool(Name = "script_write")]
    [McpMeta(McpTools.RequiresUserInteraction, true)]
    [Description(Text)]
    public async Task<string> WriteAsync(
        [Description("El id del script, el nombre de su carpeta.")] string id,
        [Description("La ficha entera, como objeto JSON de script.json (name, entry, description, timeoutSec y lo demás).")] JsonElement manifest,
        [Description("La huella que dio scripts_read; vacía para crear.")] string expectedFingerprint,
        [Description("Los archivos de texto a escribir, cada uno con name y text.")] ScriptFileText[]? files = null,
        CancellationToken cancellationToken = default) =>
        ToolOutput.Clip(await pipe.CallAsync(
            RequestNames.ScriptsWrite, new { id, manifest, expectedFingerprint, files }, PipeClient.RequestTimeout, cancellationToken).ConfigureAwait(false));
}

/// <summary>Un archivo que escribe script_write: el nombre, sin carpetas, y su texto.</summary>
public sealed record ScriptFileText(
    [property: Description("El nombre del archivo, con extensión .cmd, .ps1 o .py.")] string Name,
    [property: Description("El texto entero del archivo.")] string Text);
