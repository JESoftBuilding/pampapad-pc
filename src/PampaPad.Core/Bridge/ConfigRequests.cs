namespace PampaPad.Core;

/// <summary>
/// Guardar la configuración y recargarla (05 §4.1, §4.2 y §11): config.save y library.reload. config.save recibe la configuración entera,
/// escribe los parámetros de cada paso en el orden de sus campos (ConfigCanon), junta los problemas que dependen del catálogo contra esa
/// misma configuración (ConfigCheck.Steps: page.go busca su página en la candidata) y se la da a Library.SaveAsync, que decide la huella,
/// el resto de los problemas y el archivo. Lo que no frena se guarda y vuelve en la respuesta, con los atajos que Windows no dejó
/// registrar: Library.Changed corre Dispatcher.Refresh antes de que SaveAsync vuelva, así que el estado ya los tiene.
/// action.save y key.assign, los del MCP que cambian una pieza, van por el mismo camino (ConfigEdits; plan de la etapa MCP, E-MCP-6).
/// </summary>
public static class ConfigRequests
{
    public static void Register(Bridge bridge, EditSources sources)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(sources);
        bridge.Register<ConfigSaveRequest, ConfigSaveResponse>(
            RequestNames.ConfigSave, RequestKind.Write, Doors.Window, (request, caller, _) => ConfigEdits.SaveAsync(sources, request.Config, request.ExpectedFingerprint, caller), RequestChecks.ConfigSave);
        bridge.Register<ActionSaveRequest, ConfigSaveResponse>(
            RequestNames.ActionSave, RequestKind.Write, Doors.Mcp,
            (request, caller, _) => ConfigEdits.EditAsync(sources, config => ConfigEdits.PutAction(config, request.Action), request.ExpectedFingerprint, caller), RequestChecks.ActionSave);
        bridge.Register<KeyAssignRequest, ConfigSaveResponse>(
            RequestNames.KeyAssign, RequestKind.Write, Doors.Mcp,
            (request, caller, _) => ConfigEdits.EditAsync(sources, config => ConfigEdits.AssignKey(config, request), request.ExpectedFingerprint, caller), RequestChecks.KeyAssign);
        bridge.Register<NoData, LibraryReloadResponse>(
            RequestNames.LibraryReload, RequestKind.Write, Doors.WindowAndTray, (_, caller, ct) => ReloadAsync(sources, caller, ct));
    }

    // Leer el archivo puede tardar: fuera del hilo que pide, como el "Recargar biblioteca" de la bandeja.
    private static async Task<Result<LibraryReloadResponse>> ReloadAsync(EditSources sources, Caller caller, CancellationToken ct)
    {
        IReadOnlyList<Problem> problems = await Task.Run(sources.Library.Load, ct).ConfigureAwait(false);
        sources.Log.Write(LogLevel.Info, "bridge", $"Biblioteca recargada: estado {sources.Library.Status}; problemas {problems.Count}.", null, BridgePolicy.AsTrigger(caller));
        return new LibraryReloadResponse(sources.Library.Fingerprint, problems);
    }
}
