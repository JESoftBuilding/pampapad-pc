namespace PampaPad.Core;

/// <summary>Lo que necesitan los pedidos y los eventos de la pantalla Estado. Lo arma Composition; Version es la de la cáscara.</summary>
public sealed record StatusSources(
    string Version,
    StateStore State,
    Library Library,
    Dispatcher Dispatcher,
    Log Log,
    IReadOnlyList<IStepType> Catalog,
    IReadOnlyList<ConditionType> Conditions,
    Func<SettingsView> Settings);

/// <summary>
/// Los pedidos y eventos de la pantalla Estado (05 §4.4 y §11): app.snapshot, log.read y action.cancel; los eventos state, log,
/// action.finished y config.changed. El núcleo los emite haya o no quien escuche (01 §5). Register devuelve con qué soltarlos.
/// </summary>
public static class StatusRequests
{
    // Guarda por hilo: una línea escrita mientras se publica el evento log, en el mismo hilo, no vuelve a salir como evento.
    [ThreadStatic]
    private static bool _publishingLog;

    public static IDisposable Register(Bridge bridge, StatusSources sources)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(sources);

        bridge.Register<NoData, AppSnapshot>(RequestNames.AppSnapshot, RequestKind.Read, Doors.WindowAndMcp,
            (_, _, _) => Task.FromResult(Result<AppSnapshot>.Success(Snapshot(sources))));
        bridge.Register<LogReadRequest, LogReadResponse>(RequestNames.LogRead, RequestKind.Read, Doors.WindowAndMcp,
            (request, _, _) => Task.FromResult(LogQuery.Read(sources.Log.Recent(), request)));
        bridge.Register<ActionCancelRequest, ActionCancelResponse>(RequestNames.ActionCancel, RequestKind.Execute, Doors.WindowAndTray,
            (request, caller, _) => Task.FromResult(Cancel(sources, request, caller)));

        // El estado lleva su propio seq: el de este momento puede ser más nuevo que el del estado que trae el evento (01 §4.4).
        IDisposable states = sources.State.Subscribe((_, next) => bridge.Publish(EventNames.State, next.Seq, StateView.From(next)));
        Action<ActionResult> finished = result => bridge.Publish(EventNames.ActionFinished, ActionFinishedView.From(result));
        Action<Config> changed = config => bridge.Publish(EventNames.ConfigChanged, new ConfigChangedView(config));
        Action<LogEntry> written = entry => PublishLog(bridge, entry);
        sources.Dispatcher.Finished += finished;
        sources.Library.Changed += changed;
        sources.Log.Written += written;

        return new Unhook(() =>
        {
            states.Dispose();
            sources.Dispatcher.Finished -= finished;
            sources.Library.Changed -= changed;
            sources.Log.Written -= written;
        });
    }

    /// <summary>
    /// La foto de app.snapshot (01 §5). La configuración y el estado se leen por separado: si entre los dos cae una recarga, el evento
    /// config.changed trae la nueva.
    /// </summary>
    public static AppSnapshot Snapshot(StatusSources sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return new AppSnapshot(
            sources.Version,
            Migrations.CurrentVersion,
            sources.Library.Current,
            StateView.From(sources.State.Current),
            [.. sources.Catalog.Select(StepTypeView.From)],
            [.. sources.Conditions.Select(ConditionTypeView.From)],
            sources.Settings());
    }

    private static Result<ActionCancelResponse> Cancel(StatusSources sources, ActionCancelRequest request, Caller caller)
    {
        if (request.All == request.RunId.HasValue)
        {
            return Result<ActionCancelResponse>.Fail(ErrorCodes.InvalidData, "action.cancel lleva un runId o \"all\": true, uno de los dos.", "runId");
        }

        int cancelled = request.RunId is { } runId ? (sources.Dispatcher.Cancel(runId) ? 1 : 0) : sources.Dispatcher.CancelAll();
        sources.Log.Write(
            LogLevel.Info, "bridge", $"Cancelar {(request.All ? "todas" : "una")}: {cancelled} en curso.", null, BridgePolicy.AsTrigger(caller), request.RunId);
        return new ActionCancelResponse(cancelled);
    }

    private static void PublishLog(Bridge bridge, LogEntry entry)
    {
        if (_publishingLog)
        {
            return;
        }

        _publishingLog = true;
        try
        {
            bridge.Publish(EventNames.Log, entry);
        }
        finally
        {
            _publishingLog = false;
        }
    }

    private sealed class Unhook(System.Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
