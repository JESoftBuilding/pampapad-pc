using System.ComponentModel;
using ModelContextProtocol.Server;
using PampaPad.Core;

namespace PampaPad.Mcp.Tools;

/// <summary>scripts_read (05 §7): scripts.list o, con un id, scripts.read con el contenido.</summary>
public sealed class ScriptsRead(PipeClient pipe)
{
    private const string Text = """
        Lee los scripts de la biblioteca (la carpeta scripts/). Sin id, la lista: ficha, estado, qué acciones y teclas lo usan, la
        última corrida y los problemas. Con un id, ese script: su ficha cruda, sus archivos con el texto de los .cmd, .ps1 y .py de
        hasta 256 KB, las últimas corridas y su huella en "fingerprint". Se pide por id, nunca por ruta.
        La huella cubre la ficha y los archivos de texto: es la que pide script_write. Si alguno cambió desde que se leyó (por ejemplo,
        Emmanuel editó el .cmd a mano), script_write falla con config_changed_on_disk y hay que volver a leer con esta herramienta.
        Anda siempre, también grabando.
        """;

    [McpServerTool(Name = "scripts_read", ReadOnly = true)]
    [Description(Text)]
    public async Task<string> ReadAsync(
        [Description("El id del script, el nombre de su carpeta en scripts/. Sin id, la lista.")] string? id = null,
        CancellationToken cancellationToken = default) =>
        ToolOutput.Clip(id is null
            ? await pipe.CallAsync(RequestNames.ScriptsList, new { }, PipeClient.RequestTimeout, cancellationToken).ConfigureAwait(false)
            : await pipe.CallAsync(RequestNames.ScriptsRead, new { id, content = true }, PipeClient.RequestTimeout, cancellationToken).ConfigureAwait(false));
}
