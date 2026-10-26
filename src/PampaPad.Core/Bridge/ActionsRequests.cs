namespace PampaPad.Core;

/// <summary>
/// Lo que la pantalla Acciones le pide al núcleo además de guardar (05 §4.2 y §11): probar un borrador. action.test corre la acción tal
/// como está en la ventana, sin guardarla, con los pasos de verdad (cambia la escena, corre el script, cambia de página), y contesta
/// en el acto con el runId: el tope de espera de la página no tiene que cortar una acción larga (05 §4). El resultado llega por
/// action.finished, y se cancela con action.cancel. Con una toma en curso no corre (BridgePolicy). Cuando entren las listas de los
/// campos (E7-7), options.list se registra acá.
/// </summary>
public static class ActionsRequests
{
    public static void Register(Bridge bridge, EditSources sources)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(sources);
        bridge.Register<ActionTestRequest, ActionTestResponse>(
            RequestNames.ActionTest, RequestKind.Execute, Doors.Window, (request, caller, _) => Task.FromResult(Test(sources, request, caller)));
    }

    private static Result<ActionTestResponse> Test(EditSources sources, ActionTestRequest request, Caller caller)
    {
        var runId = Guid.NewGuid();
        var invocation = new Invocation(runId, BridgePolicy.AsTrigger(caller), "window", null, null, null);
        Failure? refused = sources.Dispatcher.LaunchDraft(request.Action, invocation);
        return refused is null ? new ActionTestResponse(runId) : refused;
    }
}
