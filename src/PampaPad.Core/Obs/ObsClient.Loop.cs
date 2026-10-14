namespace PampaPad.Core;

// El bucle de reintentos de ObsClient (spec 03 §2): abrir, sostener la sesión, esperar y volver a abrir. Está aparte
// por el tope de 300 líneas; es la misma clase, y los campos y los parámetros del constructor están en ObsClient.cs.
public sealed partial class ObsClient
{
    public async Task KeepConnectedAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await AttemptAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                Warn($"falla inesperada en el enlace con OBS ({e.GetType().Name})", ErrorCodes.InternalError);
                await PauseAsync(RetryDelay, ct).ConfigureAwait(false);
            }
        }

        Info("el bucle de OBS terminó: se pidió el apagado");
    }

    /// <summary>Fuerza un intento ya: despierta al bucle si está esperando y corta la conexión si la hay.</summary>
    public void Reconnect()
    {
        if (_wake.CurrentCount == 0)
        {
            _wake.Release();
        }

        Volatile.Read(ref _connection)?.Abort();
    }

    // Un intento: abrir, sostener la sesión hasta que se corte y dejar anotado cómo quedó el enlace.
    // Dirección y clave se leen en cada intento (spec 03 §2).
    private async Task AttemptAsync(CancellationToken ct)
    {
        var uri = address();
        var key = password();
        var (outcome, opened, detail) = await ObsConnection.OpenAsync(uri, key, clock, Warn, ct).ConfigureAwait(false);
        if (outcome == ObsOpenOutcome.AuthFailed)
        {
            SetLink(ObsLinkState.AuthFailed, "OBS rechazó la clave (cierre 4009)");
            await WaitForNewCredentialsAsync(uri, key, ct).ConfigureAwait(false);
            return;
        }

        if (opened is null)
        {
            SetLink(AfterFailure(hadSession: false), detail);
            await PauseAsync(RetryDelay, ct).ConfigureAwait(false);
            return;
        }

        var closeCode = await RunSessionAsync(opened, ct).ConfigureAwait(false);
        if (closeCode == ObsMessages.CloseSessionInvalidated)
        {
            SetLink(ObsLinkState.Kicked, "OBS cerró la sesión (cierre 4011)");
            await _wake.WaitAsync(ct).ConfigureAwait(false);        // no reconecta sola: espera a Reconnect
            return;
        }

        SetLink(AfterFailure(hadSession: true), "se cortó la conexión");
        await PauseAsync(RetryDelay, ct).ConfigureAwait(false);
    }

    // auth_failed no reintenta en bucle: vuelve a probar si cambió lo que da el archivo de OBS o si se pide Reconnect.
    private async Task WaitForNewCredentialsAsync(Uri uri, string? key, CancellationToken ct)
    {
        while (!await PauseAsync(RetryDelay, ct).ConfigureAwait(false))
        {
            if (address() != uri || password() != key)
            {
                return;
            }
        }
    }

    // Espera hasta ese tiempo. Devuelve true si la espera se cortó antes: alguien llamó a Reconnect o se pidió el apagado.
    private async Task<bool> PauseAsync(TimeSpan time, CancellationToken ct)
    {
        using var timer = new CancellationTokenSource(time, clock);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);
        try
        {
            await _wake.WaitAsync(both.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return ct.IsCancellationRequested;      // false: pasó el tiempo, que es lo normal
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
}
