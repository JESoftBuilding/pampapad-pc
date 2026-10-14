using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PampaPad.Tests.Doubles;

// El servidor: el saludo HTTP de WebSocket, el bucle de cada sesión y el saludo y la autenticación de obs-websocket.
public sealed partial class FakeObs
{
    // El doble hace la cuenta por su lado, como relevar_obs.py: no usa la función del producto.
    private static string ExpectedAuthentication(string password)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + Salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + Challenge)));
    }

    // El saludo HTTP de WebSocket (RFC 6455), a mano: lee el pedido y contesta 101 con la clave de aceptación.
    private static async Task<WebSocket?> UpgradeAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8 * 1024];
        var total = 0;
        string request;
        do
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
            {
                return null;
            }

            total += read;
            request = Encoding.ASCII.GetString(buffer, 0, total);
        }
        while (!request.Contains("\r\n\r\n", StringComparison.Ordinal) && total < buffer.Length);

        var key = request.Split("\r\n")
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim().Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1].Trim())
            .FirstOrDefault();
        if (key is null)
        {
            return null;
        }

#pragma warning disable CA5350 // SHA-1 lo pide el protocolo WebSocket para este saludo; no protege nada
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
#pragma warning restore CA5350
        var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct);
        return WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
    }

    // El próximo mensaje de texto, o null si el cliente cerró.
    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var text = new MemoryStream();
        while (true)
        {
            var part = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (part.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            text.Write(buffer, 0, part.Count);
            if (part.EndOfMessage)
            {
                return Encoding.UTF8.GetString(text.ToArray());
            }
        }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var tcp = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = ServeAsync(tcp, Interlocked.Increment(ref _accepted));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // el doble se detuvo: es el final normal de este bucle
        }
    }

    private async Task ServeAsync(TcpClient tcp, int id)
    {
        try
        {
            var socket = await UpgradeAsync(tcp.GetStream(), _stop.Token);
            if (socket is null)
            {
                return;
            }

            var session = new Session(tcp, socket);
            _sessions[id] = session;
            await session.SendAsync(HelloMessage(), _stop.Token);
            var buffer = new byte[64 * 1024];
            while (await ReceiveTextAsync(socket, buffer, _stop.Token) is { } text)
            {
                if (session.Frozen)
                {
                    continue;
                }

                await HandleAsync(session, JsonNode.Parse(text)!.AsObject());
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
            // el cliente se fue o la prueba cortó la sesión: es lo que estas pruebas provocan
        }
        finally
        {
            _sessions.TryRemove(id, out _);
            tcp.Dispose();
        }
    }

    private async Task HandleAsync(Session session, JsonObject message)
    {
        var d = message["d"]!.AsObject();
        switch ((int)message["op"]!)
        {
            case 1:
                await IdentifyAsync(session, d);
                break;
            case 6:
                await AnswerRequestAsync(session, d);
                break;
            case 8:
                await AnswerBatchAsync(session, d);
                break;
        }
    }

    private async Task IdentifyAsync(Session session, JsonObject d)
    {
        if (Password is { } password && (string?)d["authentication"] != ExpectedAuthentication(password))
        {
            await session.CloseAsync(4009, "Authentication failed.", _stop.Token);
            return;
        }

        session.Subscriptions = (int?)d["eventSubscriptions"] ?? 2047;
        session.IsIdentified = true;
        Interlocked.Increment(ref _identified);
        await session.SendAsync(Envelope(2, new JsonObject { ["negotiatedRpcVersion"] = 1 }), _stop.Token);
    }

    private string HelloMessage()
    {
        var d = new JsonObject { ["obsStudioVersion"] = "32.2.2", ["obsWebSocketVersion"] = "5.7.4", ["rpcVersion"] = 1 };
        if (Password is not null)
        {
            d["authentication"] = new JsonObject { ["challenge"] = Challenge, ["salt"] = Salt };
        }

        return Envelope(0, d);
    }

    private sealed class Session(TcpClient tcp, WebSocket socket)
    {
        private readonly SemaphoreSlim _sending = new(1, 1);

        public bool IsIdentified { get; set; }

        public int Subscriptions { get; set; }

        public bool Frozen { get; set; }

        public async Task SendAsync(string json, CancellationToken ct)
        {
            await _sending.WaitAsync(ct);
            try
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                _sending.Release();
            }
        }

        public async Task CloseAsync(int code, string reason, CancellationToken ct)
        {
            await _sending.WaitAsync(ct);
            try
            {
                await socket.CloseOutputAsync((WebSocketCloseStatus)code, reason, ct);
            }
            finally
            {
                _sending.Release();
            }
        }

        public void Drop()
        {
            socket.Abort();
            tcp.Dispose();
        }
    }
}
