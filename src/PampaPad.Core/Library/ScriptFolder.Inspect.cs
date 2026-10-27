namespace PampaPad.Core;

/// <summary>Un problema de un script, con su código de ProblemCodes (05 §4.3). Sin ubicación: es del script entero.</summary>
public sealed record ScriptProblem(string Code, string Message);

/// <summary>
/// Una cosa que hay en scripts/: una carpeta con ficha, una carpeta sin ficha o un archivo suelto (Loose, cuyo id es su nombre con la
/// extensión). Manifest es la ficha cruda, si se pudo leer; Problems, todo lo que impide correrlo.
/// </summary>
public sealed record ScriptFound(string Id, string Path, bool Loose, ScriptManifest? Manifest, IReadOnlyList<ScriptProblem> Problems)
{
    /// <summary>Un archivo suelto o una carpeta sin script.json: la pantalla lo muestra como "Sin ficha", no como "Problema".</summary>
    public bool WithoutManifest => Loose || Problems.Any(problem => problem.Code == ProblemCodes.ManifestMissing);
}

// Inspect: la pantalla Scripts ve todo lo que hay, también lo que no corre (etapa 8). Read sigue siendo lo que usan el paso y options.list.
public static partial class ScriptFolder
{
    /// <summary>Una fila por cosa que hay en scripts/, por id y en orden. Sin biblioteca o sin carpeta, la lista vacía.</summary>
    public static IReadOnlyList<ScriptFound> Inspect(string libraryFolder)
    {
        string root = string.IsNullOrEmpty(libraryFolder) ? "" : Path.Combine(libraryFolder, FolderName);
        if (root.Length == 0 || !Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return [.. Directory.EnumerateFileSystemEntries(root).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).Select(id => Inspect(libraryFolder, id)).OfType<ScriptFound>()];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>La fila de una sola cosa de scripts/, o null si no hay nada con ese nombre. El nombre no puede salir de scripts/.</summary>
    public static ScriptFound? Inspect(string libraryFolder, string id)
    {
        if (string.IsNullOrEmpty(libraryFolder) || string.IsNullOrEmpty(id) || id is "." or ".." || Path.GetFileName(id) != id
            || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        string path = Path.Combine(libraryFolder, FolderName, id);
        if (File.Exists(path))
        {
            return new ScriptFound(id, path, Loose: true, null,
                [new ScriptProblem(ProblemCodes.ManifestMissing, $"\"{id}\" es un archivo suelto: un script es una carpeta scripts\\<id> con su {ManifestName}.")]);
        }

        return Directory.Exists(path) ? InspectFolder(path, id) : null;
    }

    private static ScriptFound InspectFolder(string folder, string id)
    {
        var problems = new List<ScriptProblem>();
        if (id.Length > Validator.MaxIdLength || !IdFormat().IsMatch(id))
        {
            problems.Add(new ScriptProblem(ProblemCodes.IdInvalid, $"\"{id}\" no sirve de id: la carpeta lleva minúsculas, números y guiones."));
        }

        string file = Path.Combine(folder, ManifestName);
        if (!File.Exists(file))
        {
            problems.Add(new ScriptProblem(ProblemCodes.ManifestMissing, $"Falta {ManifestName}: sin ficha no se puede correr ni elegir en un paso."));
            return new ScriptFound(id, folder, Loose: false, null, problems);
        }

        string? unreadable = ReadManifest(file, id, out ScriptManifest? manifest);
        if (unreadable is not null || manifest is null)
        {
            problems.Add(new ScriptProblem(ProblemCodes.ManifestUnreadable, unreadable ?? $"La ficha del script \"{id}\" está vacía."));
            return new ScriptFound(id, folder, Loose: false, null, problems);
        }

        if (manifest.Entry is not { Length: > 0 } entry)
        {
            problems.Add(new ScriptProblem(ProblemCodes.EntryMissing, "La ficha no dice qué archivo correr: falta \"entry\"."));
        }
        else if (ResolveEntry(folder, id, entry, out string path) is { } outside)
        {
            problems.Add(new ScriptProblem(ProblemCodes.EntryMissing, outside.Message));
        }
        else
        {
            if (!File.Exists(path))
            {
                problems.Add(new ScriptProblem(ProblemCodes.EntryMissing, $"No existe el archivo \"{entry}\" de la entrada."));
            }

            if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                problems.Add(new ScriptProblem(ProblemCodes.NoInterpreter, $"No hay con qué correr \"{entry}\": van .cmd, .ps1, .py o .exe."));
            }
        }

        if (manifest.Contract is > KnownContract)
        {
            problems.Add(new ScriptProblem(ProblemCodes.ContractNewer, $"Pide la versión {manifest.Contract} del contrato y este programa conoce la {KnownContract}: actualizalo."));
        }

        return new ScriptFound(id, folder, Loose: false, manifest, problems);
    }
}
