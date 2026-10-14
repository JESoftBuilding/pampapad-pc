namespace PampaPad.Core;

// Cliente de obs-websocket v5 (spec 03 §2). Sostiene el enlace con OBS y publica su estado en el StateStore.
// Los números son los de 03 §2 y §5 y viven sólo acá. La clave de OBS no se anota nunca: del enlace sólo
// se escriben textos fijos, versiones y el nombre del tipo de una excepción.
public sealed class ObsClient(
    Func<Uri> address,
    Func<string?> password,
    StateStore state,
    Log log,
    TimeProvider clock,
    Func<bool>? obsProcessRunning = null) : IAsyncDisposable
{
    /// <summary>Tope de espera de un pedido.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Reintento en closed, no_websocket y lost.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>Reintento de la lectura en loading.</summary>
    public static readonly TimeSpan LoadingRetry = TimeSpan.FromSeconds(1);

    /// <summary>Sondeo grabando o en pausa.</summary>
    public static readonly TimeSpan PollRecording = TimeSpan.FromSeconds(1);

    /// <summary>Latido con la grabación detenida.</summary>
    public static readonly TimeSpan PollIdle = TimeSpan.FromSeconds(5);

    /// <summary>Pedidos vencidos seguidos que dan lost.</summary>
    public const int TimeoutsForLost = 2;

    /// <summary>Máscara de eventos del Identify (spec 03 §6): General, Config, Scenes, Inputs, Outputs y SceneItems.</summary>
    public const int EventSubscriptions = 207;

    private ObsConnection? _connection;
    private volatile bool _exitAnnounced;

    /// <summary>Se dispara en el hilo de recepción, en el orden en que OBS mandó los eventos. Quien se suscribe sólo encola.</summary>
    public event Action<ObsEvent>? Event;

    public async Task KeepConnectedAsync(CancellationToken ct)
    {
        try
        {
            var (_, opened, detail) = await ObsConnection.OpenAsync(address(), password(), clock, Warn, ct).ConfigureAwait(false);
            if (opened is null)
            {
                SetLink(AfterFailure(hadSession: false), detail);
                return;
            }

            await RunSessionAsync(opened, ct).ConfigureAwait(false);
            SetLink(AfterFailure(hadSession: true), "se cortó la conexión");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Info("el bucle de OBS terminó: se pidió el apagado");
        }
    }

    /// <summary>Nunca tira. Sin enlace en ready no manda nada (spec 03 §3, "Falla de cualquier paso de OBS").</summary>
    public async Task<ObsResponse> RequestAsync(string requestType, object? data, CancellationToken ct)
    {
        var live = Volatile.Read(ref _connection);
        if (live is null || state.Current.Obs.Link != ObsLinkState.Ready)
        {
            return ObsResponse.Fail(ErrorCodes.ObsDisconnected);
        }

        return await live.RequestAsync(requestType, data, null, ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        Volatile.Read(ref _connection)?.Abort();
        return ValueTask.CompletedTask;
    }

    // Una sesión: desde que OBS identifica hasta que el socket se cierra. Devuelve el código de cierre.
    private async Task<int?> RunSessionAsync(ObsConnection live, CancellationToken ct)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            _exitAnnounced = false;
            Volatile.Write(ref _connection, live);
            SetLink(ObsLinkState.Loading, $"OBS {live.Hello.ObsVersion}, obs-websocket {live.Hello.WebSocketVersion}");
            var watching = WatchAsync(live, session.Token);
            await live.RunAsync(OnEvent, ct).ConfigureAwait(false);         // vuelve cuando el socket se cierra
            await session.CancelAsync().ConfigureAwait(false);
            await watching.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return live.CloseCode;
        }
        finally
        {
            Volatile.Write(ref _connection, null);
            await live.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task WatchAsync(ObsConnection live, CancellationToken ct)
    {
        if (await ReadInitialAsync(live, ct).ConfigureAwait(false))
        {
            BecomeReady();
        }
    }

    // Paso 4 del saludo (spec 03 §2): GetVersion dice si este OBS tiene los pedidos que se usan.
    private async Task<bool> ReadInitialAsync(ObsConnection live, CancellationToken ct)
    {
        var version = await live.RequestAsync(
            "GetVersion",
            null,
            reply => Update(obs => obs with { SupportsMarkers = ObsMessages.Supports(reply, "CreateRecordChapter") }),
            ct).ConfigureAwait(false);
        return version.Ok;
    }

    // Corre en el hilo de recepción, en el orden en que OBS mandó los eventos.
    private void OnEvent(ObsEvent e)
    {
        switch (e.Type)
        {
            case "ExitStarted":
                _exitAnnounced = true;
                SetLink(ObsLinkState.Exiting, "OBS avisó que se cierra");
                break;
            case "CurrentSceneCollectionChanging":
                SetLink(ObsLinkState.Switching, "OBS está cambiando de colección");
                break;
            case "CurrentSceneCollectionChanged":
                SetLink(ObsLinkState.Loading, "OBS terminó de cambiar de colección: se relee todo");
                break;
        }

        try
        {
            Event?.Invoke(e);
        }
        catch (Exception failure)
        {
            Warn($"un suscriptor de eventos de OBS falló con {failure.GetType().Name} en {e.Type}", ErrorCodes.InternalError);
        }
    }

    // Cómo queda el enlace cuando no hay conexión (spec 03 §2): closed, exiting, lost o no_websocket.
    private ObsLinkState AfterFailure(bool hadSession)
    {
        if (obsProcessRunning?.Invoke() != true)
        {
            _exitAnnounced = false;
            return ObsLinkState.Closed;
        }

        if (_exitAnnounced)
        {
            return ObsLinkState.Exiting;
        }

        return hadSession ? ObsLinkState.Lost : ObsLinkState.NoWebsocket;
    }

    // ready es "lectura inicial completa" (spec 03 §2). Sólo se pasa desde loading: si en el medio llegó un
    // cambio de colección o el aviso de cierre, manda ese estado.
    private void BecomeReady()
    {
        var promoted = false;
        state.Update(s =>
        {
            promoted = s.Obs.Link == ObsLinkState.Loading;
            return promoted ? s with { Obs = s.Obs.WithLink(ObsLinkState.Ready) } : s;
        });
        if (promoted)
        {
            Info("enlace con OBS: Ready (lectura inicial completa)");
        }
    }

    // Anota el cambio una sola vez: con OBS cerrado el bucle reintenta cada 2 s y no llena el registro.
    private void SetLink(ObsLinkState link, string why)
    {
        if (state.Current.Obs.Link == link)
        {
            return;
        }

        state.Update(s => s with { Obs = s.Obs.WithLink(link) });
        Info($"enlace con OBS: {link} ({why})");
    }

    private void Update(Func<ObsState, ObsState> change) => state.Update(s => s with { Obs = change(s.Obs) });

    private void Info(string message) => log.Write(LogLevel.Info, "obs", message);

    private void Warn(string message) => log.Write(LogLevel.Warning, "obs", message);

    private void Warn(string message, string code) => log.Write(LogLevel.Warning, "obs", message, code);
}
