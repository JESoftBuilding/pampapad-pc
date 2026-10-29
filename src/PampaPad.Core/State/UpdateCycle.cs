using System.Threading.Channels;

namespace PampaPad.Core;

/// <summary>
/// Lo que el ciclo le pide a quien sabe bajar e instalar. La cáscara lo arma sobre Velopack; las pruebas, a mano.
/// Ninguno de los tres tira: devuelven la falla como dato.
/// </summary>
/// <param name="Find">Busca una versión más nueva que la instalada. Devuelve su número, o null si no hay.</param>
/// <param name="Download">Baja la versión que encontró la última búsqueda.</param>
/// <param name="ApplyOnExit">Deja la versión bajada lista para instalarse cuando el programa salga, y para volver a abrirlo.</param>
public sealed record UpdateFeed(
    Func<CancellationToken, Task<Result<string?>>> Find,
    Func<CancellationToken, Task<Result>> Download,
    Func<Result> ApplyOnExit);

/// <summary>
/// La máquina de estados de la actualización (05 §8): busca a los 15 s y cada 6 h, ofrece, y sólo baja e instala cuando
/// se lo piden y la política de reinicio deja. Publica su fase en AppState.Update.
/// </summary>
public sealed class UpdateCycle(
    StateStore state,
    LocalSettings settings,
    Log log,
    TimeProvider clock,
    UpdateFeed feed,
    Func<AppState, RestartVerdict> policy)
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim _busy = new(1, 1);

    /// <summary>El bucle de vida del proceso. Termina cuando se cancela.</summary>
    public async Task KeepCheckingAsync(CancellationToken ct)
    {
        var ticks = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        using var timer = clock.CreateTimer(_ => ticks.Writer.TryWrite(true), null, FirstCheckDelay, CheckInterval);
        try
        {
            await foreach (var tick in ticks.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await CheckAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            log.Info("update", "Se detuvo la búsqueda periódica de versiones");
        }
    }

    /// <summary>Busca una versión nueva y, si hay y no está salteada, la ofrece. No baja nada.</summary>
    public async Task<Result> CheckAsync(CancellationToken ct)
    {
        if (!_busy.Wait(0))
        {
            // Ya hay una búsqueda o una instalación en curso: vale lo que salga de esa.
            return Result.Success;
        }

        try
        {
            var before = state.Current.Update;
            if (before.Phase == UpdatePhase.Applying)
            {
                // La versión ya quedó entregada para instalarse al salir: el programa está cerrando y la oferta no se reabre.
                return Result.Success;
            }

            Publish(before with { Phase = UpdatePhase.Checking });
            var found = await feed.Find(ct).ConfigureAwait(false);
            if (!found.Ok)
            {
                // Sin red no se avisa: queda acá y se reintenta en el próximo ciclo (05 §8).
                log.Warning("update", "No se pudo buscar una versión nueva: " + found.Failure.Message, found.Failure.Code);
                Publish(before);
                return found.Failure;
            }

            var version = found.Value;
            if (version is null)
            {
                Publish(UpdateState.Idle);
            }
            else if (version == settings.Current.SkippedVersion)
            {
                log.Info("update", $"La versión {version} está salteada en local.json: no se ofrece");
                Publish(UpdateState.Idle);
            }
            else
            {
                if (before.Version != version)
                {
                    log.Info("update", $"Hay una versión nueva: {version}");
                }

                Publish(new UpdateState(UpdatePhase.Available, version));
            }

            return Result.Success;
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>
    /// Baja la versión ofrecida y la deja lista para instalarse al salir. Con resultado bueno, quien llamó cierra el programa.
    /// Sólo se llama porque Emmanuel lo pidió: el ciclo nunca la llama solo.
    /// </summary>
    public async Task<Result> DownloadAndApplyAsync(CancellationToken ct)
    {
        if (!_busy.Wait(0))
        {
            return Result.Fail(ErrorCodes.Cancelled, "Ya hay una búsqueda o una instalación en curso");
        }

        try
        {
            var offer = state.Current.Update;
            if (offer.Phase != UpdatePhase.Available || offer.Version is null)
            {
                return Result.Fail(ErrorCodes.InvalidData, "No hay una versión lista para instalar");
            }

            var refusal = Refusal(offer.Version);
            if (refusal is not null)
            {
                return refusal;
            }

            Publish(new UpdateState(UpdatePhase.Downloading, offer.Version));
            var downloaded = await feed.Download(ct).ConfigureAwait(false);
            if (!downloaded.Ok)
            {
                return BackToOffer(offer.Version, "No se pudo bajar", downloaded.Failure);
            }

            // Pudo empezar una toma mientras bajaba: se vuelve a preguntar antes de aplicar.
            refusal = Refusal(offer.Version);
            if (refusal is not null)
            {
                return refusal;
            }

            Publish(new UpdateState(UpdatePhase.Applying, offer.Version));
            var applied = feed.ApplyOnExit();
            if (!applied.Ok)
            {
                return BackToOffer(offer.Version, "No se pudo instalar", applied.Failure);
            }

            // Velopack mata lo que corre en current, también PampaPad.Mcp.exe, y Claude Code no reconecta solo un servidor stdio (05 §7).
            log.Info("update", $"La versión {offer.Version} se instala al salir. Las sesiones de Claude con el MCP se reconectan desde /mcp");
            return Result.Success;
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>Guarda en local.json que la versión ofrecida no se quiere, y retira la oferta.</summary>
    public async Task<Result> SkipAsync(CancellationToken ct)
    {
        var offer = state.Current.Update;
        if (offer.Phase != UpdatePhase.Available || offer.Version is null)
        {
            return Result.Fail(ErrorCodes.InvalidData, "No hay una versión para saltear");
        }

        var version = offer.Version;
        var saved = await settings.SaveAsync(data => data with { SkippedVersion = version }, ct).ConfigureAwait(false);
        if (!saved.Ok)
        {
            return saved;
        }

        log.Info("update", $"Se saltea la versión {version}");
        Publish(UpdateState.Idle);
        return Result.Success;
    }

    /// <summary>
    /// Si la política no deja, la oferta vuelve a quedar como estaba y el motivo va al registro y a la falla, no al estado:
    /// la bandeja lo calcula de AppState cada vez que pinta (TraySummary.UpdateItems), así que se va solo cuando la toma
    /// termina. Guardado en Detail duraba hasta la próxima búsqueda buena (revisión de la etapa 2).
    /// </summary>
    private Failure? Refusal(string version)
    {
        var verdict = policy(state.Current);
        if (verdict.Allowed)
        {
            return null;
        }

        var reason = verdict.Reason ?? RestartPolicy.UnnamedReason;
        log.Info("update", $"No se instala la versión {version}: {reason}");
        Publish(new UpdateState(UpdatePhase.Available, version));
        return new Failure(ErrorCodes.ReadOnlyWhileRecording, reason);
    }

    private Result BackToOffer(string version, string what, Failure failure)
    {
        log.Error("update", $"{what} la versión {version}: {failure.Message}", failure.Code);
        Publish(new UpdateState(UpdatePhase.Available, version, $"{what}: {failure.Message}"));
        return failure;
    }

    private void Publish(UpdateState update) => state.Update(current => current with { Update = update });
}
