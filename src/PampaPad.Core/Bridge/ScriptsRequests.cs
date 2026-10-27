namespace PampaPad.Core;

/// <summary>
/// Lo que necesitan los pedidos de la pantalla Scripts: la biblioteca, el runner (qué está corriendo) y las últimas corridas, del
/// registro. Lo arma Composition.
/// </summary>
public sealed record ScriptsSources(Library Library, ScriptRunner Runner, ScriptRuns Runs);

/// <summary>
/// Los pedidos de la pantalla Scripts (05 §4.3 y §11). scripts.list trae la lista entera, que la página filtra; scripts.read, con el
/// id, suma lo que muestra la ficha. Leen el disco en cada pedido: un script copiado a mano se ve sin recargar.
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
