namespace PampaPad.Core;

/// <summary>
/// Lo que la pantalla Teclas le pide al núcleo además de guardar (05 §4.1 y §6): suspender y reanudar los atajos globales mientras la
/// página graba una combinación. Es windowOnly: por el MCP contesta no_permission (BridgePolicy). Con una toma en curso no se suspende:
/// las marcas quedarían sin atajos justo mientras se graba (05, Incierto, "si se puede capturar un atajo mientras se graba"); reanudar
/// sí se puede siempre, porque lo pide la página para dejar todo como estaba.
/// </summary>
public static class KeysRequests
{
    public static void Register(Bridge bridge, EditSources sources)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(sources);
        bridge.Register<HotkeysSuspendRequest, NoData>(
            RequestNames.HotkeysSuspend, RequestKind.WindowOnly, Doors.Window, (request, _, _) => Task.FromResult(Suspend(sources, request)));
    }

    private static Result<NoData> Suspend(EditSources sources, HotkeysSuspendRequest request)
    {
        ObsState obs = sources.State.Current.Obs;
        if (request.Suspended && (obs.TakeInProgress || obs.Streaming))
        {
            return Result<NoData>.Fail(
                ErrorCodes.ReadOnlyWhileRecording,
                "No se graba un atajo mientras se graba, se está en pausa o se transmite: los atajos de las marcas quedarían sin andar.");
        }

        // La copia de desarrollo no toma los atajos (05 §10): no hay nada que suspender y la página no tiene por qué saberlo.
        sources.Hotkeys?.Suspend(request.Suspended);
        return new NoData();
    }
}
