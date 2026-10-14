using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json.Nodes;

namespace PampaPad.Tests.Doubles;

/// <summary>
/// OBS falso (spec 01 §7 y 03 §9): un servidor WebSocket de verdad, dentro del proceso de pruebas, que habla
/// obs-websocket v5. Escucha con TcpListener en el puerto 0, con lo que el sistema elige uno libre, y hace a
/// mano el saludo HTTP de WebSocket: HttpListener no admite el puerto 0. La clase está en cuatro archivos por
/// el tope de 300 líneas: este (la puerta y el ciclo de vida), FakeObs.Server.cs (el saludo y las sesiones),
/// FakeObs.State.cs (el estado de OBS que imita) y FakeObs.Requests.cs (lo que contesta cada pedido).
/// </summary>
public sealed partial class FakeObs : IAsyncDisposable
{
    public const string Salt = "sal-de-prueba";
    public const string Challenge = "desafio-de-prueba";
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, byte> _silent = new();
    private readonly Task _accepting;
    private int _accepted;
    private int _identified;
    private int _disposed;

    private FakeObs(TcpListener listener, string? password)
    {
        _listener = listener;
        Password = password;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _accepting = AcceptAsync();
    }

    public int Port { get; }

    public Uri Address => new($"ws://127.0.0.1:{Port}");

    /// <summary>Null: el saludo no pide clave.</summary>
    public string? Password { get; set; }

    /// <summary>Todo pedido contesta 207, como OBS cargando.</summary>
    public bool Loading { get; set; }

    /// <summary>Conexiones que llegaron.</summary>
    public int Accepted => Volatile.Read(ref _accepted);

    /// <summary>Sesiones que pasaron el saludo.</summary>
    public int Identified => Volatile.Read(ref _identified);

    /// <summary>El requestType de cada pedido que llegó, en orden.</summary>
    public ConcurrentQueue<string> Received { get; } = new();

    /// <summary>Con port 0 el sistema elige un puerto libre; con un número, vuelve a abrir en ese.</summary>
    public static FakeObs Start(int port = 0, string? password = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return new FakeObs(listener, password);
    }

    /// <summary>Un puerto libre y cerrado: para probar "OBS todavía no está".</summary>
    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public int Times(string requestType) => Received.Count(type => type == requestType);

    /// <summary>El pedido llega y nadie lo contesta: para probar el tope de espera.</summary>
    public void DoNotAnswer(string requestType) => _silent[requestType] = 0;

    public void AnswerAgain(string requestType) => _silent.TryRemove(requestType, out _);

    /// <summary>Las sesiones abiertas dejan de contestar y siguen abiertas: el socket colgado después de una suspensión.</summary>
    public void FreezeSessions()
    {
        foreach (var session in _sessions.Values)
        {
            session.Frozen = true;
        }
    }

    /// <summary>Corte seco: se cae la conexión sin cierre de WebSocket y sin ExitStarted.</summary>
    public void DropSessions()
    {
        foreach (var session in _sessions.Values)
        {
            session.Drop();
        }
    }

    /// <summary>Cierre con código: 4011 es la sesión echada desde la lista de sesiones de OBS.</summary>
    public async Task CloseSessionsAsync(int code, string reason)
    {
        foreach (var session in _sessions.Values)
        {
            await session.CloseAsync(code, reason, _stop.Token);
        }
    }

    /// <summary>Manda un evento a las sesiones identificadas que se suscribieron a esa intención.</summary>
    public async Task EmitAsync(string eventType, int intent, JsonObject? data = null)
    {
        var d = new JsonObject { ["eventIntent"] = intent, ["eventType"] = eventType };
        if (data is not null)
        {
            d["eventData"] = data.DeepClone();
        }

        var json = Envelope(5, d);
        foreach (var session in _sessions.Values)
        {
            if (!session.IsIdentified || session.Frozen || (session.Subscriptions & intent) == 0)
            {
                continue;
            }

            try
            {
                await session.SendAsync(json, _stop.Token);
            }
            catch (Exception e) when (e is WebSocketException or IOException or ObjectDisposedException or InvalidOperationException)
            {
                session.Frozen = true;      // esa sesión ya se fue: no se le manda más
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync();
        _listener.Stop();
        DropSessions();
        await _accepting;
    }

    private static string Envelope(int op, JsonObject d) => new JsonObject { ["op"] = op, ["d"] = d }.ToJsonString();
}
