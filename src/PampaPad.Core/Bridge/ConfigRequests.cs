namespace PampaPad.Core;

/// <summary>
/// Guardar la configuración y recargarla (05 §4.1, §4.2 y §11): config.save y library.reload. config.save recibe la configuración entera,
/// escribe los parámetros de cada paso en el orden de sus campos (ConfigCanon), junta los problemas que dependen del catálogo contra esa
/// misma configuración (ConfigCheck.Steps: page.go busca su página en la candidata) y se la da a Library.SaveAsync, que decide la huella,
/// el resto de los problemas y el archivo. Lo que no frena se guarda y vuelve en la respuesta, con los atajos que Windows no dejó
/// registrar: Library.Changed corre Dispatcher.Refresh antes de que SaveAsync vuelva, así que el estado ya los tiene.
/// </summary>
public static class ConfigRequests
{
    public static void Register(Bridge bridge, EditSources sources)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(sources);
        bridge.Register<ConfigSaveRequest, ConfigSaveResponse>(
            RequestNames.ConfigSave, RequestKind.Write, Doors.Window, (request, caller, ct) => SaveAsync(sources, request, caller, ct));
        bridge.Register<NoData, LibraryReloadResponse>(
            RequestNames.LibraryReload, RequestKind.Write, Doors.WindowAndTray, (_, caller, ct) => ReloadAsync(sources, caller, ct));
    }

    private static async Task<Result<ConfigSaveResponse>> SaveAsync(EditSources sources, ConfigSaveRequest request, Caller caller, CancellationToken ct)
    {
        Config ordered = ConfigCanon.Order(request.Config, sources.Steps, sources.Conditions);
        IReadOnlyList<Problem> fromSteps = ConfigCheck.Steps(ordered, sources.Executor, sources.Log);
        Result saved = await sources.Library.SaveAsync(ordered, request.ExpectedFingerprint, fromSteps, ct).ConfigureAwait(false);
        if (!saved.Ok)
        {
            sources.Log.Write(LogLevel.Warning, "bridge", $"No se guardó config.json: {saved.Failure.Message}", saved.Failure.Code, BridgePolicy.AsTrigger(caller));
            return saved.Failure;
        }

        IReadOnlyList<Problem> remaining = sources.State.Current.Problems;
        sources.Log.Write(LogLevel.Info, "bridge", $"Se guardó config.json ({remaining.Count} problemas que no frenan).", null, BridgePolicy.AsTrigger(caller));
        return new ConfigSaveResponse(sources.Library.Fingerprint, remaining);
    }

    // Leer el archivo puede tardar: fuera del hilo que pide, como el "Recargar biblioteca" de la bandeja.
    private static async Task<Result<LibraryReloadResponse>> ReloadAsync(EditSources sources, Caller caller, CancellationToken ct)
    {
        IReadOnlyList<Problem> problems = await Task.Run(sources.Library.Load, ct).ConfigureAwait(false);
        sources.Log.Write(LogLevel.Info, "bridge", $"Biblioteca recargada: estado {sources.Library.Status}; problemas {problems.Count}.", null, BridgePolicy.AsTrigger(caller));
        return new LibraryReloadResponse(sources.Library.Fingerprint, problems);
    }
}
