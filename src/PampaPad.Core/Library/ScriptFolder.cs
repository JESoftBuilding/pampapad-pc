using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PampaPad.Core;

/// <summary>Un script encontrado: su id, su carpeta, la ruta completa del entry y la ficha.</summary>
public sealed record ScriptInfo(string Id, string Folder, string EntryPath, ScriptManifest Manifest);

/// <summary>
/// Encuentra un script por su id en scripts/&lt;id&gt;/ de la biblioteca y lee su ficha (02 §9). Nunca sale de la carpeta del script:
/// un id con otros caracteres o un entry con ".." o con una ruta absoluta no se resuelven (01 §3.1, Scripts/). No ejecuta nada.
/// </summary>
public static partial class ScriptFolder
{
    public const string FolderName = "scripts";
    public const string ManifestName = "script.json";

    /// <summary>La versión del contrato de ejecución que conoce el programa (02 §9).</summary>
    public const int KnownContract = 1;

    /// <summary>Las extensiones que corre el contrato 1 (02 §9, punto 1).</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".cmd", ".ps1", ".py", ".exe"];

    /// <summary>
    /// Los scripts de scripts/ cuya ficha se puede leer, por id y en orden. Los que tienen la ficha rota o ninguna no están: son los que
    /// script.run no puede correr, y los muestra la pantalla Scripts con su problema (etapa 8). Sin biblioteca o sin carpeta, la lista vacía.
    /// </summary>
    public static IReadOnlyList<ScriptInfo> List(string libraryFolder)
    {
        string root = string.IsNullOrEmpty(libraryFolder) ? "" : Path.Combine(libraryFolder, FolderName);
        if (root.Length == 0 || !Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return
            [
                .. Directory.EnumerateDirectories(root)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Order(StringComparer.Ordinal)
                    .Select(id => Read(libraryFolder, id))
                    .Where(read => read.Ok)
                    .Select(read => read.Value!),
            ];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static Result<ScriptInfo> Read(string libraryFolder, string id)
    {
        if (string.IsNullOrEmpty(libraryFolder))
        {
            return Fail(ErrorCodes.ScriptNotFound, $"No hay biblioteca: el script \"{id}\" no se puede buscar.");
        }

        if (string.IsNullOrEmpty(id) || id.Length > Validator.MaxIdLength || !IdFormat().IsMatch(id))
        {
            return Fail(ErrorCodes.ScriptNotFound, $"\"{id}\" no es un id de script: van minúsculas, números y guiones, como el nombre de su carpeta.");
        }

        string folder = Path.Combine(libraryFolder, FolderName, id);
        if (!Directory.Exists(folder))
        {
            return Fail(ErrorCodes.ScriptNotFound, $"No existe el script \"{id}\": falta la carpeta scripts\\{id} en la biblioteca.");
        }

        string file = Path.Combine(folder, ManifestName);
        if (!File.Exists(file))
        {
            return Fail(ErrorCodes.ScriptWithoutManifest, $"El script \"{id}\" no tiene ficha: falta scripts\\{id}\\{ManifestName}.");
        }

        ScriptManifest? manifest;
        try
        {
            byte[] bytes = File.ReadAllBytes(file);
            int skip = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
            manifest = JsonSerializer.Deserialize<ScriptManifest>(bytes.AsSpan(skip), Json.Options);
        }
        catch (JsonException error)
        {
            // Como config.json (Library.Read): la línea y la columna contadas desde 1, y el campo cuando lo que falla es el tipo de un valor.
            string field = error.Path is { Length: > 1 } where ? $" ({where})" : "";
            return Fail(ErrorCodes.ScriptWithoutManifest,
                $"La ficha del script \"{id}\" no se puede leer: error en la línea {(error.LineNumber ?? 0) + 1}, columna {(error.BytePositionInLine ?? 0) + 1}{field}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Fail(ErrorCodes.ScriptWithoutManifest, $"La ficha del script \"{id}\" no se puede leer: {error.Message}");
        }

        if (manifest?.Entry is not { Length: > 0 } entry)
        {
            return Fail(ErrorCodes.ScriptWithoutManifest, $"La ficha del script \"{id}\" no dice qué archivo correr: falta \"entry\".");
        }

        if (Path.IsPathRooted(entry) || entry.Split('/', '\\').Contains(".."))
        {
            return Fail(ErrorCodes.ScriptNotFound, $"El entry \"{entry}\" del script \"{id}\" sale de su carpeta: va el nombre de un archivo de scripts\\{id}.");
        }

        string path;
        string inside;
        try
        {
            path = Path.GetFullPath(Path.Combine(folder, entry));
            inside = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        {
            // Un carácter nulo o decenas de miles de caracteres son texto válido en JSON y no una ruta: es otra ficha rota (02 §10, problema 6).
            return Fail(ErrorCodes.ScriptNotFound, $"El entry del script \"{id}\" no es una ruta de archivo: va el nombre de un archivo de scripts\\{id}.");
        }

        // La segunda defensa mira la ruta ya resuelta: un nombre de dispositivo como NUL pasa la de arriba y Windows lo lleva a \\.\NUL.
        if (!path.StartsWith(inside, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(ErrorCodes.ScriptNotFound, $"El entry \"{entry}\" del script \"{id}\" no queda dentro de scripts\\{id}: va el nombre de un archivo de esa carpeta.");
        }

        if (!File.Exists(path))
        {
            return Fail(ErrorCodes.ScriptNotFound, $"No existe el archivo \"{entry}\" del script \"{id}\".");
        }

        if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return Fail(ErrorCodes.ScriptFailed, $"El script \"{id}\" no se puede correr: van .cmd, .ps1, .py o .exe, y entry es \"{entry}\".");
        }

        if (manifest.Contract is > KnownContract)
        {
            return Fail(ErrorCodes.ScriptFailed, $"El script \"{id}\" pide la versión {manifest.Contract} del contrato y este programa conoce la {KnownContract}: actualizalo.");
        }

        return new ScriptInfo(id, folder, path, manifest);
    }

    // El formato de los otros ids (02 §4.2 y §9), con \z como en Validator.IdFormat.
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex IdFormat();

    private static Result<ScriptInfo> Fail(string code, string message) => new Failure(code, message);
}
