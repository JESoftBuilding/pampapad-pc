namespace PampaPad.Core;

/// <summary>
/// Lo que necesitan los pedidos de la pantalla Scripts: la biblioteca, el runner (qué está corriendo), las últimas corridas, del
/// registro, el diálogo y el Explorador de Windows, la carpeta del respaldo local (otro disco que la biblioteca) y el reloj, que fecha
/// la carpeta de un script quitado. Lo arma Composition.
/// </summary>
public sealed record ScriptsSources(Library Library, ScriptRunner Runner, ScriptRuns Runs, IShellDialogs Shell, string BackupFolder, TimeProvider Clock);

/// <summary>
/// Los pedidos de la pantalla Scripts (05 §4.3 y §11). scripts.list trae la lista entera, que la página filtra; scripts.read, con el
/// id, suma lo que muestra la ficha. Leen el disco en cada pedido: un script copiado a mano se ve sin recargar. Los que escriben están
/// en ScriptEdits; los tres frenan con una toma en curso (BridgePolicy). library.reveal es de la tarjeta Datos de Ajustes, y va acá
/// porque usa el mismo Explorador.
/// </summary>
public static class ScriptsRequests
{
    /// <summary>Cuántas corridas trae scripts.read (05 §4.3: "las cinco últimas corridas").</summary>
    public const int RunsRead = 5;

    public static void Register(Bridge bridge, ScriptsSources sources)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(sources);
        bridge.Register<NoData, ScriptsListResponse>(RequestNames.ScriptsList, RequestKind.Read, Doors.WindowAndMcp,
            (_, _, _) => Task.FromResult(Result<ScriptsListResponse>.Success(new ScriptsListResponse(ScriptCatalog.List(sources)))));
        bridge.Register<ScriptsReadRequest, ScriptsReadResponse>(RequestNames.ScriptsRead, RequestKind.Read, Doors.WindowAndMcp,
            (request, _, _) => Task.FromResult(Read(sources, request.Id ?? "")));
        bridge.Register<ScriptsWriteRequest, ScriptsWriteResponse>(RequestNames.ScriptsWrite, RequestKind.Write, Doors.WindowAndMcp,
            (request, _, _) => Task.FromResult(ScriptEdits.Write(sources, request)));
        bridge.Register<NoData, ScriptsImportResponse>(RequestNames.ScriptsImport, RequestKind.Write, Doors.Window,
            (_, _, _) => ScriptEdits.ImportAsync(sources));
        bridge.Register<ScriptsDeleteRequest, ScriptsDeleteResponse>(RequestNames.ScriptsDelete, RequestKind.Write, Doors.Window,
            (request, _, _) => Task.FromResult(ScriptEdits.Delete(sources, request.Id ?? "")));
        bridge.Register<ScriptsRevealRequest, NoData>(RequestNames.ScriptsReveal, RequestKind.Execute, Doors.Window,
            (request, _, _) => Task.FromResult(Reveal(sources, request.Id)));
        bridge.Register<NoData, NoData>(RequestNames.LibraryReveal, RequestKind.WindowOnly, Doors.Window,
            (_, _, _) => Task.FromResult(RevealLibrary(sources)));
    }

    // Con el id, la carpeta del script; sin id, scripts/, que se crea si falta: el Explorador con una carpeta que no está abre otra.
    private static Result<NoData> Reveal(ScriptsSources sources, string? id)
    {
        string folder = sources.Library.Folder;
        if (folder.Length == 0)
        {
            return Result<NoData>.Fail(ErrorCodes.ScriptNotFound, "No hay una carpeta de biblioteca elegida: no hay scripts que mostrar.");
        }

        string path;
        if (id is null)
        {
            path = Path.Combine(folder, ScriptFolder.FolderName);
            Directory.CreateDirectory(path);
        }
        else if (ScriptFolder.Inspect(folder, id) is { Loose: false } found)
        {
            path = found.Path;
        }
        else
        {
            return Result<NoData>.Fail(ErrorCodes.ScriptNotFound, $"No existe el script \"{id}\": falta la carpeta scripts\\{id}.", "id");
        }

        sources.Shell.Reveal(path);
        return new NoData();
    }

    private static Result<NoData> RevealLibrary(ScriptsSources sources)
    {
        if (sources.Library.Folder.Length == 0 || !Directory.Exists(sources.Library.Folder))
        {
            return Result<NoData>.Fail(ErrorCodes.ConfigInvalid, "No hay una carpeta de biblioteca elegida: se elige desde la bandeja, en \"Elegir carpeta de datos\".");
        }

        sources.Shell.Reveal(sources.Library.Folder);
        return new NoData();
    }

    private static Result<ScriptsReadResponse> Read(ScriptsSources sources, string id)
    {
        if (ScriptFolder.Inspect(sources.Library.Folder, id) is not { } found)
        {
            return Result<ScriptsReadResponse>.Fail(ErrorCodes.ScriptNotFound, $"No hay nada que se llame \"{id}\" en scripts\\ de la biblioteca.", "id");
        }

        if (found.Loose)
        {
            var file = new FileInfo(found.Path);
            return Result<ScriptsReadResponse>.Fail(ErrorCodes.ScriptWithoutManifest,
                $"\"{id}\" es un archivo suelto en scripts\\ ({file.Length} bytes, del {file.LastWriteTime:dd/MM/yyyy HH:mm}): un script es una carpeta con su {ScriptFolder.ManifestName}.", "id");
        }

        try
        {
            ScriptFile[] files = [.. new DirectoryInfo(found.Path).EnumerateFiles().OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(file => new ScriptFile(file.Name, file.Length, new DateTimeOffset(file.LastWriteTime)))];
            string manifest = Path.Combine(found.Path, ScriptFolder.ManifestName);
            string fingerprint = File.Exists(manifest) ? Fingerprints.Of(File.ReadAllBytes(manifest)) : "";
            ScriptView view = ScriptCatalog.View(found, sources.Library.Current, sources.Runner.Running, sources.Runs);
            return new ScriptsReadResponse(view, found.Manifest, files, fingerprint, [.. sources.Runs.Last(id, RunsRead).Select(ScriptCatalog.Run)]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Result<ScriptsReadResponse>.Fail(ErrorCodes.ScriptNotFound, $"La carpeta del script \"{id}\" no se puede leer: {error.Message}", "id");
        }
    }
}
