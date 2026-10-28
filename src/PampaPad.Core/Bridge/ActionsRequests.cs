using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// Lo que la pantalla Acciones le pide al núcleo además de guardar (05 §4.2 y §11): probar un borrador. action.test corre la acción tal
/// como está en la ventana, sin guardarla, con los pasos de verdad (cambia la escena, corre el script, cambia de página), y contesta
/// en el acto con el runId: el tope de espera de la página no tiene que cortar una acción larga (05 §4). El resultado llega por
/// action.finished, y se cancela con action.cancel. Con una toma en curso no corre (BridgePolicy). options.list da las opciones de los
/// campos de un paso: las escenas y entradas de OBS que ya refleja el estado, los scripts de la biblioteca y las ventanas abiertas.
/// </summary>
public static class ActionsRequests
{
    public static void Register(Bridge bridge, EditSources sources)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(sources);
        bridge.Register<ActionTestRequest, ActionTestResponse>(
            RequestNames.ActionTest, RequestKind.Execute, Doors.Window, (request, caller, _) => Task.FromResult(Test(sources, request, caller)));
        bridge.Register<OptionsListRequest, OptionsListResponse>(
            RequestNames.OptionsList, RequestKind.Read, Doors.WindowAndMcp, (request, _, _) => Task.FromResult(Options(sources, request)));
        bridge.Register<ScriptsTestRequest, ActionTestResponse>(
            RequestNames.ScriptsTest, RequestKind.Execute, Doors.Window, (request, caller, _) => Task.FromResult(TestScript(sources, request.Id ?? "", caller)));
    }

    // Probar un script es probar un borrador de un paso, sin id: usa DraftKey, así que no corre junto con la prueba de una acción sin id
    // (plan de la etapa 8, decisión 1). Va acá y no en ScriptsRequests porque necesita el despachador. Lo que falta se niega antes de lanzar.
    private static Result<ActionTestResponse> TestScript(EditSources sources, string id, Caller caller)
    {
        if (sources.Library.ReadScript(id) is { Ok: false, Failure: { } missing })
        {
            return missing with { Where = "id" };
        }

        Step step = JsonSerializer.Deserialize<Step>(JsonSerializer.Serialize(new { type = ScriptRun.TypeId, script = id, wait = true }), Json.Options)!;
        return Test(sources, new ActionTestRequest(new Action { Name = $"Probar {id}", Steps = [step] }), caller);
    }

    private static Result<OptionsListResponse> Options(EditSources sources, OptionsListRequest request)
    {
        ObsState obs = sources.State.Current.Obs;
        return request.List switch
        {
            OptionsList.Scenes => FromObs(obs, obs.Scenes),
            OptionsList.Inputs => FromObs(obs, obs.Inputs),
            OptionsList.Scripts => new OptionsListResponse([.. sources.Library.ListScripts().Select(script => new OptionItem(script.Id, Detail: script.Manifest.Name))]),
            OptionsList.Windows => new OptionsListResponse(
                [.. sources.Windows.List().Select(window => new OptionItem(window.Title, Detail: window.Process)).DistinctBy(item => (item.Name, item.Detail)).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)]),
            _ => Result<OptionsListResponse>.Fail(ErrorCodes.InvalidData, $"No hay una lista {request.List}: van scenes, inputs, scripts o windows.", "list"),
        };
    }

    // Sin enlace con OBS las listas del estado pueden ser de la conexión anterior: no se ofrecen, y el campo acepta texto (05 §4.2).
    private static Result<OptionsListResponse> FromObs(ObsState obs, IReadOnlyList<ObsItem> items) =>
        obs.Link == ObsLinkState.Ready
            ? new OptionsListResponse([.. items.Select(item => new OptionItem(item.Name, item.Uuid))])
            : new OptionsListResponse([], $"OBS no está conectado ({ObsView.LinkName(obs.Link)}): el campo acepta el nombre escrito a mano.");

    private static Result<ActionTestResponse> Test(EditSources sources, ActionTestRequest request, Caller caller)
    {
        var runId = Guid.NewGuid();
        var invocation = new Invocation(runId, BridgePolicy.AsTrigger(caller), "window", null, null, null);
        Failure? refused = sources.Dispatcher.LaunchDraft(request.Action, invocation);
        return refused is null ? new ActionTestResponse(runId) : refused;
    }
}
