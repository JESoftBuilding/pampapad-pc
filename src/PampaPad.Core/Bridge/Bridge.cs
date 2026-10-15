using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// La tabla de nombre de pedido a manejador (01 §3.1 y §5). Recibe texto y devuelve texto, sin suponer hilo ni un solo cliente:
/// la misma puerta sirve a la ventana, a la bandeja y, más adelante, al MCP. HandleAsync nunca deja pasar una excepción (01 §4.1).
/// Los eventos van a quien esté suscripto, haya o no alguien (01 §5).
/// </summary>
public sealed class Bridge(StateStore state, Log log)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Registration> _handlers = new(StringComparer.Ordinal);
    private Action<string>[] _sinks = [];

    /// <summary>
    /// Si el MCP puede pedir. Hasta que exista el interruptor "Permitir que Claude maneje PampaPad" de Ajustes (05 §4.5; su clave de
    /// local.json no tiene nombre, pregunta abierta 16), no puede: toda llamada mcp da no_permission.
    /// </summary>
    public Func<bool> McpAllowed { get; init; } = static () => false;

    public IReadOnlyCollection<string> Registered
    {
        get
        {
            lock (_gate)
            {
                return [.. _handlers.Keys];
            }
        }
    }

    /// <summary>Los pedidos cuya puerta incluye a quien llama. Internal puede siempre (01 §5).</summary>
    public IReadOnlyCollection<string> RegisteredFor(Caller caller)
    {
        lock (_gate)
        {
            return [.. _handlers.Values.Where(registration => caller == Caller.Internal || registration.Callers.Contains(caller)).Select(registration => registration.Name)];
        }
    }

    public void Register<TReq, TRes>(string name, RequestKind kind, IReadOnlySet<Caller> callers, Func<TReq, Caller, CancellationToken, Task<Result<TRes>>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callers);
        ArgumentNullException.ThrowIfNull(handler);

        async Task<Outcome> InvokeAsync(JsonElement data, Caller caller, CancellationToken ct)
        {
            TReq? request;
            try
            {
                request = data.Deserialize<TReq>(Json.Compact);
            }
            catch (JsonException e)
            {
                // Los datos de la página son entrada no confiable (01 §5): un campo de más o de otro tipo no se despacha.
                log.Warning("bridge", $"Datos que no sirven para {name}: {e.Message}", ErrorCodes.InvalidData);
                return new Outcome(null, new Failure(ErrorCodes.InvalidData, $"Los datos de {name} no sirven: {e.Message}", e.Path));
            }

            if (request is null)
            {
                return new Outcome(null, new Failure(ErrorCodes.InvalidData, $"{name} necesita datos.", "data"));
            }

            Result<TRes> result = await handler(request, caller, ct).ConfigureAwait(false);
            return result.Ok ? new Outcome(result.Value, null) : new Outcome(null, result.Failure);
        }

        lock (_gate)
        {
            if (!_handlers.TryAdd(name, new Registration(name, kind, callers, InvokeAsync)))
            {
                throw new InvalidOperationException($"El pedido {name} ya tiene manejador.");
            }
        }
    }

    public async Task<string> HandleAsync(string messageJson, Caller caller, CancellationToken ct)
    {
        long id = 0;
        string name = "(sin nombre)";
        try
        {
            Result<IncomingRequest> parsed = Envelope.Parse(messageJson);
            if (!parsed.Ok)
            {
                return Refuse(Envelope.IdOf(messageJson), parsed.Failure, caller);
            }

            IncomingRequest request = parsed.Value!;
            id = request.Id;
            name = request.Request;
            Registration? registration;
            lock (_gate)
            {
                _handlers.TryGetValue(name, out registration);
            }

            if (registration is null)
            {
                return Refuse(id, new Failure(ErrorCodes.UnknownRequest, $"No existe el pedido {name}.", "request"), caller);
            }

            Failure? refused = BridgePolicy.Check(name, registration.Kind, registration.Callers, caller, PolicyContext.From(state.Current, McpAllowed()));
            if (refused is not null)
            {
                return Refuse(id, refused, caller);
            }

            Outcome outcome = await registration.Invoke(request.Data, caller, ct).ConfigureAwait(false);
            return outcome.Failure is not null
                ? Envelope.Error(id, outcome.Failure)
                : Envelope.Response(id, outcome.Data ?? new NoData());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Envelope.Error(id, new Failure(ErrorCodes.Cancelled, $"Se canceló {name}: se cerró quien lo pidió."));
        }
        catch (Exception e)
        {
            // La línea lleva la excepción entera: con sólo el mensaje, un "Object reference not set" no dice dónde pasó.
            log.Error("bridge", $"El pedido {name} reventó: {e}", ErrorCodes.InternalError);
            return Envelope.Error(id, new Failure(ErrorCodes.InternalError, $"El pedido {name} falló adentro del programa: {e.Message}"));
        }
    }

    public IDisposable Subscribe(Action<string> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (_gate)
        {
            _sinks = [.. _sinks, sink];
        }

        return new Subscription(this, sink);
    }

    /// <summary>Publica con el seq del estado de este momento. Para log, action.finished y config.changed.</summary>
    public void Publish(string eventName, object data) => Publish(eventName, state.Current.Seq, data);

    /// <summary>Publica con un seq dado: el evento state lleva el del estado que trae, no el de este momento (01 §4.4).</summary>
    public void Publish(string eventName, long seq, object data)
    {
        string json;
        try
        {
            json = Envelope.Event(eventName, seq, data);
        }
        catch (Exception e)
        {
            log.Error("bridge", $"No se pudo armar el evento {eventName}: {e}", ErrorCodes.InternalError);
            return;
        }

        foreach (Action<string> sink in Volatile.Read(ref _sinks))
        {
            try
            {
                sink(json);
            }
            catch (Exception e)
            {
                // Se suelta antes de anotar: si no, la línea de este error saldría como evento log y volvería a este mismo suscriptor.
                Unsubscribe(sink);
                log.Error("bridge", $"Un suscriptor del puente tiró y se soltó: {e}", ErrorCodes.InternalError);
            }
        }
    }

    private string Refuse(long id, Failure failure, Caller caller)
    {
        log.Warning("bridge", $"Pedido de {caller} rechazado: {failure.Message}", failure.Code);
        return Envelope.Error(id, failure);
    }

    private void Unsubscribe(Action<string> sink)
    {
        lock (_gate)
        {
            _sinks = [.. _sinks.Where(candidate => candidate != sink)];
        }
    }

    private sealed record Registration(string Name, RequestKind Kind, IReadOnlySet<Caller> Callers, Func<JsonElement, Caller, CancellationToken, Task<Outcome>> Invoke);

    private readonly record struct Outcome(object? Data, Failure? Failure);

    private sealed class Subscription(Bridge bridge, Action<string> sink) : IDisposable
    {
        public void Dispose() => bridge.Unsubscribe(sink);
    }
}
