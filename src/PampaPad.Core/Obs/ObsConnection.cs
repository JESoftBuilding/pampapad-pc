using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PampaPad.Core;

internal enum ObsOpenOutcome { Connected, Refused, AuthFailed }

// Una conexión viva con obs-websocket: el saludo, los envíos de a uno y la recepción, que reparte las
// respuestas por requestId y los eventos. No reconecta ni sabe de estados: eso es de ObsClient.
internal sealed class ObsConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly TimeProvider _clock;
    private readonly Action<string> _note;
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly ConcurrentDictionary<string, Waiting> _waiting = new();
    private readonly ArrayBufferWriter<byte> _buffer = new(16 * 1024);
    private long _lastId;
    private int _timeoutsInARow;
    private volatile bool _closed;

    private ObsConnection(TimeProvider clock, Action<string> note)
    {
        _clock = clock;
        _note = note;
        _socket.Options.Proxy = null;                                   // es local: sin proxy del sistema
        _socket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;   // el latido es el sondeo de ObsClient
    }

    public ObsHello Hello { get; private set; }

    /// <summary>Con qué código cerró OBS; null si se cortó sin cierre.</summary>
    public int? CloseCode { get; private set; }

    /// <summary>
    /// Abre el socket y hace el saludo (spec 03 §2, pasos 1 a 3). Todo el saludo tiene el tope de un pedido.
    /// La clave sólo se usa para la cuenta de authentication: no se guarda ni se anota.
    /// </summary>
    public static async Task<(ObsOpenOutcome Outcome, ObsConnection? Connection, string Detail)> OpenAsync(
        Uri address, string? password, TimeProvider clock, Action<string> note, CancellationToken ct)
    {
        var connection = new ObsConnection(clock, note);
        using var limit = new CancellationTokenSource(ObsClient.RequestTimeout, clock);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        try
        {
            await connection._socket.ConnectAsync(address, both.Token).ConfigureAwait(false);
            if (await connection.ReadAsync(both.Token).ConfigureAwait(false) is not { Op: ObsMessages.OpHello } hello)
            {
                return await connection.GiveUpAsync(ObsOpenOutcome.Refused, "no llegó el saludo de OBS").ConfigureAwait(false);
            }

            connection.Hello = ObsMessages.ToHello(hello.D);
            var authentication = connection.Hello is { Challenge: { } challenge, Salt: { } salt }
                ? ObsMessages.Authentication(password ?? string.Empty, salt, challenge)
                : null;
            await connection.SendAsync(ObsMessages.Identify(ObsClient.EventSubscriptions, authentication), both.Token).ConfigureAwait(false);
            if (await connection.ReadAsync(both.Token).ConfigureAwait(false) is { Op: ObsMessages.OpIdentified })
            {
                return (ObsOpenOutcome.Connected, connection, "identificado");
            }

            var rejected = connection.CloseCode == ObsMessages.CloseAuthenticationFailed;
            return await connection.GiveUpAsync(
                rejected ? ObsOpenOutcome.AuthFailed : ObsOpenOutcome.Refused,
                $"OBS cerró en el saludo con el código {connection.CloseCode}").ConfigureAwait(false);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or InvalidOperationException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return (ObsOpenOutcome.Refused, null, e.GetType().Name);
        }
    }

    /// <summary>Recibe hasta que el socket se cierra. Respuestas y eventos se reparten acá, en el orden en que llegaron.</summary>
    public async Task RunAsync(Action<ObsEvent> onEvent, CancellationToken ct)
    {
        try
        {
            while (await ReadAsync(ct).ConfigureAwait(false) is { } message)
            {
                switch (message.Op)
                {
                    case ObsMessages.OpEvent:
                        onEvent(ObsMessages.ToEvent(message.D));
                        break;
                    case ObsMessages.OpRequestResponse or ObsMessages.OpRequestBatchResponse:
                        Interlocked.Exchange(ref _timeoutsInARow, 0);
                        if (ObsMessages.RequestId(message.D) is { } id && _waiting.TryRemove(id, out var entry))
                        {
                            entry.InOrder?.Invoke(message.D);
                            entry.Reply.TrySetResult(message.D);
                        }

                        break;
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or InvalidOperationException)
        {
            if (!ct.IsCancellationRequested)
            {
                _note($"la conexión con OBS se cortó sin cierre ({e.GetType().Name})");     // apagar el programa no es un corte
            }
        }
        finally
        {
            _closed = true;
            foreach (var entry in _waiting.Values)
            {
                entry.Reply.TrySetResult(null);     // los pendientes terminan en obs_disconnected
            }
        }
    }

    /// <summary>
    /// inOrder corre en el hilo de recepción, antes que cualquier mensaje posterior: es por donde el estado
    /// se actualiza sin desordenarse respecto de los eventos.
    /// </summary>
    public async Task<ObsResponse> RequestAsync(string requestType, object? data, Action<ObsResponse>? inOrder, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _lastId).ToString(CultureInfo.InvariantCulture);
        Action<JsonElement>? apply = inOrder is null ? null : reply => inOrder(ObsMessages.ToResponse(reply));
        var (error, d) = await ExchangeAsync(id, ObsMessages.Request(id, requestType, data), apply, ct).ConfigureAwait(false);
        return error is null ? ObsMessages.ToResponse(d) : ObsResponse.Fail(error);
    }

    /// <summary>
    /// Un lote (op 8): OBS procesa los pedidos en orden y contesta todo junto. Si el lote no llegó,
    /// cada pedido lleva la misma falla, así la lista siempre tiene el largo de lo pedido.
    /// </summary>
    public async Task<IReadOnlyList<ObsResponse>> BatchAsync(
        IReadOnlyList<ObsRequest> requests, Action<IReadOnlyList<ObsResponse>>? inOrder, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _lastId).ToString(CultureInfo.InvariantCulture);
        Action<JsonElement>? apply = inOrder is null ? null : reply => inOrder(ObsMessages.ToBatch(reply));
        var (error, d) = await ExchangeAsync(id, ObsMessages.Batch(id, requests), apply, ct).ConfigureAwait(false);
        if (error is null)
        {
            return ObsMessages.ToBatch(d);
        }

        return [.. requests.Select(_ => ObsResponse.Fail(error))];
    }

    public void Abort() => _socket.Abort();

    public ValueTask DisposeAsync()
    {
        _closed = true;
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<(ObsOpenOutcome Outcome, ObsConnection? Connection, string Detail)> GiveUpAsync(ObsOpenOutcome outcome, string detail)
    {
        await DisposeAsync().ConfigureAwait(false);
        return (outcome, null, detail);
    }

    // Manda y espera la respuesta con ese id. Devuelve el código de ErrorCodes si no llegó.
    private async Task<(string? Error, JsonElement D)> ExchangeAsync(string id, string json, Action<JsonElement>? inOrder, CancellationToken ct)
    {
        var entry = new Waiting(new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously), inOrder);
        _waiting[id] = entry;
        using var limit = new CancellationTokenSource(ObsClient.RequestTimeout, _clock);    // el tope corre desde antes de mandar
        using var both = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        try
        {
            if (_closed)
            {
                return (ErrorCodes.ObsDisconnected, default);
            }

            await SendAsync(json, limit.Token).ConfigureAwait(false);
            if (await entry.Reply.Task.WaitAsync(both.Token).ConfigureAwait(false) is { } d)
            {
                return (null, d);
            }

            return (ErrorCodes.ObsDisconnected, default);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (ErrorCodes.Cancelled, default);
        }
        catch (OperationCanceledException)
        {
            // Venció el tope. Con dos seguidos el socket está colgado: se corta y ObsClient reconecta (spec 03 §2, lost).
            if (Interlocked.Increment(ref _timeoutsInARow) >= ObsClient.TimeoutsForLost)
            {
                _socket.Abort();
            }

            return (ErrorCodes.ObsTimeout, default);
        }
        catch (Exception e) when (e is WebSocketException or IOException or InvalidOperationException)
        {
            return (ErrorCodes.ObsDisconnected, default);
        }
        finally
        {
            _waiting.TryRemove(id, out _);
        }
    }

    // El próximo mensaje, o null si OBS cerró (CloseCode dice con qué código).
    private async ValueTask<ObsIncoming?> ReadAsync(CancellationToken ct)
    {
        while (true)
        {
            _buffer.ResetWrittenCount();
            ValueWebSocketReceiveResult part;
            do
            {
                part = await _socket.ReceiveAsync(_buffer.GetMemory(8 * 1024), ct).ConfigureAwait(false);
                if (part.MessageType == WebSocketMessageType.Close)
                {
                    CloseCode = (int?)_socket.CloseStatus;
                    return null;
                }

                _buffer.Advance(part.Count);
            }
            while (!part.EndOfMessage);

            if (ObsMessages.TryParse(_buffer.WrittenSpan, out var message))
            {
                return message;
            }

            _note("llegó de OBS un mensaje que no se pudo leer; se saltea");     // sin el contenido: podría traer un secreto
        }
    }

    private async Task SendAsync(string json, CancellationToken ct)
    {
        await _sending.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(Encoding.UTF8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        finally
        {
            _sending.Release();
        }
    }

    private sealed record Waiting(TaskCompletionSource<JsonElement?> Reply, Action<JsonElement>? InOrder);
}
