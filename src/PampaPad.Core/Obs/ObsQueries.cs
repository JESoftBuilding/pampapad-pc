using System.Text.Json;

namespace PampaPad.Core;

/// <summary>Una fuente que se ve en la escena al aire, con el tamaño nativo que da su elemento (sourceWidth, sourceHeight).</summary>
public sealed record SceneSource(string Name, string Uuid, string? InputKind, double Width, double Height);

/// <summary>
/// Las lecturas de OBS del chequeo previo y de las condiciones (03 §7). Sólo leen: no cambian nada en OBS. Se piden al evaluar y no se
/// reflejan en el estado (Propuesto: 03 §5 lo deja "en el chequeo"). Sin el enlace en ready no mandan nada y fallan con
/// obs_disconnected: esa falla es lo que deja una comprobación en skipped (02 §4.6).
/// </summary>
public sealed class ObsQueries(ObsClient obs, StateStore state)
{
    /// <summary>Hasta cuántas escenas adentro se busca. Hoy la más honda es la cámara de Pantalla, a dos (03 §7).</summary>
    public const int MaxDepth = 8;

    public async Task<Result<JsonElement>> AskAsync(string requestType, object? data, string what, CancellationToken ct)
    {
        if (Offline(what) is { } offline)
        {
            return offline;
        }

        ObsResponse response = await obs.RequestAsync(requestType, data, ct).ConfigureAwait(false);
        return response.Ok ? response.Data : ObsStep.Rejected(response, $"la lectura de {what}");
    }

    /// <summary>El nombre de la escena al aire, para los detalles.</summary>
    public string SceneName => state.Current.Obs.SceneName ?? "la escena al aire";

    /// <summary>Los elementos visibles de primer nivel de la escena al aire: los que miden el lienzo.</summary>
    public async Task<Result<IReadOnlyList<JsonElement>>> ProgramItemsAsync(CancellationToken ct)
    {
        if (Offline("los elementos de la escena al aire") is { } offline)
        {
            return offline;
        }

        if (state.Current.Obs.SceneUuid is not { } scene)
        {
            return new Failure(ErrorCodes.ObsRequestFailed, "OBS no informó qué escena está al aire.");
        }

        Result<JsonElement> answer = await AskAsync("GetSceneItemList", new { sceneUuid = scene }, "los elementos de la escena al aire", ct).ConfigureAwait(false);
        return answer.Ok
            ? Result<IReadOnlyList<JsonElement>>.Success([.. ObsMessages.Items(answer.Value, "sceneItems").Where(item => ObsMessages.Flag(item, "sceneItemEnabled"))])
            : answer.Failure;
    }

    /// <summary>
    /// Las fuentes que se ven en la escena al aire, también las de adentro de otras escenas y de grupos: la cámara de Pantalla está dos
    /// piezas adentro (03 §7, Inferido). Sólo las de elementos visibles; cada una una vez. Una escena que aparece dos veces en el camino
    /// se mira una sola vez: un ciclo no cuelga.
    /// </summary>
    public async Task<Result<IReadOnlyList<SceneSource>>> VisibleSourcesAsync(CancellationToken ct)
    {
        if (Offline("las fuentes de la escena al aire") is { } offline)
        {
            return offline;
        }

        if (state.Current.Obs.SceneUuid is not { } scene)
        {
            return new Failure(ErrorCodes.ObsRequestFailed, "OBS no informó qué escena está al aire.");
        }

        var found = new List<SceneSource>();
        Failure? failure = await WalkAsync(scene, group: false, 0, found, [], ct).ConfigureAwait(false);
        return failure is null ? Result<IReadOnlyList<SceneSource>>.Success([.. found.DistinctBy(source => source.Uuid)]) : failure;
    }

    /// <summary>Una entrada de OBS por su referencia (02 §4.8), en la lista que refleja el estado.</summary>
    public Result<ObsItem> FindInput(ObsRef wanted) =>
        ObsItems.Resolve(wanted, state.Current.Obs.Inputs) is { } input
            ? input
            : new Failure(ErrorCodes.ObsRefNotFound, $"OBS no tiene la entrada \"{wanted.Name}\".");

    private Failure? Offline(string what) =>
        state.Current.Obs.Link == ObsLinkState.Ready ? null : new Failure(ErrorCodes.ObsDisconnected, $"Sin OBS: no se pudo leer {what}.");

    private async Task<Failure?> WalkAsync(string uuid, bool group, int depth, List<SceneSource> found, HashSet<string> seen, CancellationToken ct)
    {
        if (depth > MaxDepth || !seen.Add(uuid))
        {
            return null;
        }

        Result<JsonElement> answer = await AskAsync(group ? "GetGroupSceneItemList" : "GetSceneItemList", new { sceneUuid = uuid }, "los elementos de una escena", ct).ConfigureAwait(false);
        if (!answer.Ok)
        {
            return answer.Failure;
        }

        foreach (JsonElement item in ObsMessages.Items(answer.Value, "sceneItems"))
        {
            string? source = ObsMessages.Text(item, "sourceUuid");
            if (!ObsMessages.Flag(item, "sceneItemEnabled") || source is null)
            {
                continue;
            }

            if (ObsMessages.Text(item, "sourceType") == "OBS_SOURCE_TYPE_SCENE")
            {
                if (await WalkAsync(source, ObsMessages.Flag(item, "isGroup"), depth + 1, found, seen, ct).ConfigureAwait(false) is { } failure)
                {
                    return failure;
                }

                continue;
            }

            ObsMessages.Has(item, "sceneItemTransform", out JsonElement transform);
            found.Add(new SceneSource(
                ObsMessages.Text(item, "sourceName") ?? "",
                source,
                ObsMessages.Text(item, "inputKind"),
                ObsMessages.Number(transform, "sourceWidth"),
                ObsMessages.Number(transform, "sourceHeight")));
        }

        return null;
    }
}
