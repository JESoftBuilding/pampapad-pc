namespace PampaPad.Core;

/// <summary>
/// Guardar config.json por el camino de config.save (ConfigCanon.Order, ConfigCheck.Steps y Library.SaveAsync), entero o cambiando una
/// pieza (action.save y key.assign, plan de la etapa MCP). La escritura no recibe el token del pedido: la tubería lo cancela si el
/// cliente se va, y un guardado que empezó tiene que terminar.
/// </summary>
public static class ConfigEdits
{
    /// <summary>
    /// Aplica change a la configuración en memoria y guarda. La huella que mandó quien pide tiene que ser la de esa configuración (si no,
    /// el cambio pisaría lo que no leyó) y la del disco, que la compara Library.SaveAsync. change devuelve la falla si no se puede aplicar.
    /// </summary>
    public static Task<Result<ConfigSaveResponse>> EditAsync(EditSources sources, Func<Config, Result<Config>> change, string expectedFingerprint, Caller caller)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(change);
        (Config current, string fingerprint) = sources.Library.Latest();
        if (fingerprint != expectedFingerprint)
        {
            return Task.FromResult(Result<ConfigSaveResponse>.Fail(
                ErrorCodes.ConfigChangedOnDisk, "config.json cambió desde que se leyó: volvé a leer la biblioteca antes de guardar.", Library.FileName));
        }

        Result<Config> next = change(current);
        return next.Ok ? SaveAsync(sources, next.Value!, expectedFingerprint, caller) : Task.FromResult<Result<ConfigSaveResponse>>(next.Failure);
    }

    public static async Task<Result<ConfigSaveResponse>> SaveAsync(EditSources sources, Config next, string expectedFingerprint, Caller caller)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Config ordered = ConfigCanon.Order(next, sources.Steps, sources.Conditions);
        IReadOnlyList<Problem> fromSteps = ConfigCheck.Steps(ordered, sources.Executor, sources.Log);
        Result saved = await sources.Library.SaveAsync(ordered, expectedFingerprint, fromSteps, CancellationToken.None).ConfigureAwait(false);
        if (!saved.Ok)
        {
            sources.Log.Write(LogLevel.Warning, "bridge", $"No se guardó config.json: {saved.Failure.Message}", saved.Failure.Code, BridgePolicy.AsTrigger(caller));
            return saved.Failure;
        }

        IReadOnlyList<Problem> remaining = sources.State.Current.Problems;
        sources.Log.Write(LogLevel.Info, "bridge", $"Se guardó config.json ({remaining.Count} problemas que no frenan).", null, BridgePolicy.AsTrigger(caller));
        return new ConfigSaveResponse(sources.Library.Fingerprint, remaining);
    }

    /// <summary>Reemplaza la acción del mismo id, en su lugar, o la agrega al final.</summary>
    public static Result<Config> PutAction(Config config, Action action)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(action);
        List<Action> actions = [.. config.Actions ?? []];
        int at = actions.FindIndex(candidate => candidate?.Id == action.Id);
        if (at < 0)
        {
            actions.Add(action);
        }
        else
        {
            actions[at] = action;
        }

        return config with { Actions = actions };
    }

    /// <summary>Asigna o vacía una tecla. Una tecla que ya estaba conserva su lugar y lo que el programa no conoce.</summary>
    public static Result<Config> AssignKey(Config config, KeyAssignRequest request)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(request);
        List<Page> pages = [.. config.Pages ?? []];
        int at = pages.FindIndex(page => page?.Id == request.Page);
        if (at < 0)
        {
            return Result<Config>.Fail(ErrorCodes.ConfigInvalid, $"No hay una página \"{request.Page}\" en config.json.", "page");
        }

        List<Key> keys = [.. pages[at].Keys ?? []];
        int index = keys.FindIndex(key => key?.Position == request.Position);
        Key? existing = index < 0 ? null : keys[index];
        if (request.Clear)
        {
            if (index >= 0)
            {
                keys.RemoveAt(index);
            }
        }
        else
        {
            Key key = (existing ?? new Key()) with
            {
                Position = request.Position,
                Label = request.Label,
                ShortLabel = request.ShortLabel,
                Action = request.Action,
                Hotkey = request.Hotkey,
                OnHold = request.OnHold is null ? null : (existing?.OnHold ?? new HoldAction()) with { Action = request.OnHold },
            };
            if (index < 0)
            {
                keys.Add(key);
            }
            else
            {
                keys[index] = key;
            }
        }

        pages[at] = pages[at] with { Keys = keys };
        return config with { Pages = pages };
    }
}
