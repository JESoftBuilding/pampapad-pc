namespace PampaPad.Core;

/// <summary>
/// La consola por cable. Lee líneas de un flujo que le abre la cáscara, saca de cada InputState los
/// eventos crudos de las teclas y se reconecta sola. En esta etapa sólo lee (01 §8, etapa 3b).
/// </summary>
public sealed partial class SerialConsole : IInputSource
{
    public const string SourceId = "console";

    /// <summary>Cada cuánto reintenta abrir el puerto (01 §3.1; Propuesto).</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly Func<CancellationToken, Task<Stream>> _open;
    private readonly StateStore _state;
    private readonly Log _log;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private Stream? _stream;
    private bool _released;
    private TaskCompletionSource _changed = NewSignal();
    private Task _loop = Task.CompletedTask;
    private string? _lastFailure;
    private int _disposed;

    public SerialConsole(Func<CancellationToken, Task<Stream>> open, StateStore state, Log log, TimeProvider clock)
    {
        _open = open;
        _state = state;
        _log = log;
        _clock = clock;
    }

    public event Action<InputEvent>? Input;

    public string Id => SourceId;

    /// <summary>Arranca el bucle de lectura y vuelve en el acto. El bucle vive hasta que se cancela el token o se llama a DisposeAsync.</summary>
    public Task StartAsync(CancellationToken ct)
    {
        var life = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        _loop = Task.Run(
            async () =>
            {
                using (life)
                {
                    await RunAsync(life.Token).ConfigureAwait(false);
                }
            },
            CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>En esta etapa la consola no dibuja nada: la página que rige no cambia lo que hace esta fuente.</summary>
    public IReadOnlyList<Problem> Apply(Page page, IReadOnlyList<HotkeyBinding> hotkeys) => [];

    /// <summary>El envío entra con las luces, en la etapa 8b. Hasta entonces existe la firma y nada sale por el cable.</summary>
    public Task<Result> SendAsync(string line, CancellationToken ct)
    {
        bool connected;
        lock (_gate)
        {
            connected = _stream is not null;
        }

        var message = connected
            ? "Esta versión no le manda nada a la consola: el envío entra con las luces."
            : "La consola no está conectada.";
        return Task.FromResult(Result.Fail(ErrorCodes.ConsoleDisconnected, message));
    }

    /// <summary>Suelta el puerto (true), para cargar firmware o abrir el monitor serie, o lo vuelve a tomar (false).</summary>
    public void Release(bool on)
    {
        Stream? toClose = null;
        TaskCompletionSource signal;
        lock (_gate)
        {
            if (_released == on)
            {
                return;
            }

            _released = on;
            if (on)
            {
                toClose = _stream;
                _stream = null;
            }

            signal = _changed;
            _changed = NewSignal();
        }

        // Una lectura pendiente no respeta el token: lo único que la corta es cerrar el puerto (01 §9).
        CloseQuietly(toClose);
        signal.TrySetResult();
        _log.Info(SourceId, on ? "Se soltó el puerto de la consola." : "Se vuelve a tomar el puerto de la consola.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        CloseStream();
        await _loop.ConfigureAwait(false);
        _stop.Dispose();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task RunAsync(CancellationToken ct)
    {
        using var closeOnStop = ct.Register(CloseStream);
        while (!ct.IsCancellationRequested)
        {
            var (released, changed) = Snapshot();
            if (released)
            {
                await WaitAsync(ConsoleLinkState.Released, Timeout.InfiniteTimeSpan, changed, ct).ConfigureAwait(false);
                continue;
            }

            string reason;
            try
            {
                var stream = await _open(ct).ConfigureAwait(false);
                if (!Adopt(stream))
                {
                    continue;
                }

                _lastFailure = null;
                Publish(ConsoleLinkState.Connected);
                _log.Info(SourceId, "La consola está conectada.");
                await ReadLinesAsync(stream, ct).ConfigureAwait(false);
                reason = "La consola cerró el puerto.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                // Puerto ocupado, placa desenchufada o puerto cerrado por Release: todo es "no hay enlace".
                reason = e.Message;
            }
            finally
            {
                CloseStream();
                ForgetKeys();
            }

            (released, changed) = Snapshot();
            if (released || ct.IsCancellationRequested)
            {
                continue;
            }

            if (reason != _lastFailure)
            {
                // Se anota una vez por motivo, no una vez cada dos segundos.
                _lastFailure = reason;
                _log.Warning(SourceId, $"Sin consola: {reason} Se reintenta cada {RetryDelay.TotalSeconds:0} s.");
            }

            await WaitAsync(ConsoleLinkState.Waiting, RetryDelay, changed, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Espera el tiempo que le digan, o a que cambie "soltada". Publica el estado recién cuando el
    /// temporizador ya existe: así, quien espera ese estado con un reloj falso lo puede adelantar sin carrera.
    /// </summary>
    private async Task WaitAsync(ConsoleLinkState publish, TimeSpan delay, Task changed, CancellationToken ct)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timer = Task.Delay(delay, _clock, wait.Token);
        Publish(publish);
        await Task.WhenAny(timer, changed).ConfigureAwait(false);
        await wait.CancelAsync().ConfigureAwait(false);
    }

    private async Task ReadLinesAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[256];
        ResetLine();
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            Consume(buffer.AsSpan(0, read));
        }
    }

    private void Publish(ConsoleLinkState next) =>
        _state.Update(s => s with { Console = s.Console with { State = next } });

    private (bool Released, Task Changed) Snapshot()
    {
        lock (_gate)
        {
            return (_released, _changed.Task);
        }
    }

    /// <summary>Se queda con el flujo recién abierto, salvo que mientras abría hayan pedido soltar el puerto.</summary>
    private bool Adopt(Stream stream)
    {
        lock (_gate)
        {
            if (!_released)
            {
                _stream = stream;
                return true;
            }
        }

        CloseQuietly(stream);
        return false;
    }

    private void CloseStream()
    {
        Stream? stream;
        lock (_gate)
        {
            stream = _stream;
            _stream = null;
        }

        CloseQuietly(stream);
    }

    /// <summary>
    /// Cierra el flujo y no deja salir el error. Con la placa desenchufada, SerialStream.Dispose tira UnauthorizedAccessException,
    /// pero el runtime ya cerró el handle en su finally: no queda nada por reintentar. El motivo del corte lo anota el bucle cuando
    /// falla la lectura. Si el error saliera, mataría el bucle (la consola quedaría "conectada" para siempre) o llegaría hasta quien
    /// suelta el puerto desde la interfaz o apaga el programa.
    /// </summary>
    private static void CloseQuietly(Stream? stream)
    {
        try
        {
            stream?.Dispose();
        }
        catch (Exception)
        {
            // Ver arriba: el puerto ya está cerrado, el error de cierre no cambia nada.
        }
    }
}
