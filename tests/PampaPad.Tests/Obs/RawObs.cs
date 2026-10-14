using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Cliente crudo de WebSocket: manda texto y lee mensajes. Sólo para probar FakeObs.</summary>
internal sealed class RawObs : IAsyncDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
    private readonly ClientWebSocket _socket = new();

    public int? CloseCode => (int?)_socket.CloseStatus;

    public static async Task<RawObs> ConnectAsync(Uri address)
    {
        var raw = new RawObs();
        raw._socket.Options.Proxy = null;
        using var limit = new CancellationTokenSource(Limit);
        await raw._socket.ConnectAsync(address, limit.Token);
        return raw;
    }

    public async Task SendAsync(string json)
    {
        using var limit = new CancellationTokenSource(Limit);
        await _socket.SendAsync(Encoding.UTF8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, limit.Token);
    }

    /// <summary>El próximo mensaje, o null si el servidor cerró (CloseCode dice con qué código).</summary>
    public async Task<JsonObject?> ReadAsync()
    {
        using var limit = new CancellationTokenSource(Limit);
        var buffer = new byte[64 * 1024];
        using var text = new MemoryStream();
        while (true)
        {
            var part = await _socket.ReceiveAsync(buffer.AsMemory(), limit.Token);
            if (part.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            text.Write(buffer, 0, part.Count);
            if (part.EndOfMessage)
            {
                return JsonNode.Parse(text.ToArray())!.AsObject();
            }
        }
    }

    /// <summary>Lee el saludo y se identifica. Devuelve lo que contestó el servidor, o null si cerró.</summary>
    public async Task<JsonObject?> IdentifyAsync(string? password = null)
    {
        var hello = await ReadAsync();
        var challenge = hello!["d"]!["authentication"];
        var authentication = challenge is null || password is null
            ? null
            : ObsMessages.Authentication(password, (string)challenge["salt"]!, (string)challenge["challenge"]!);
        await SendAsync(ObsMessages.Identify(207, authentication));
        return await ReadAsync();
    }

    /// <summary>Manda un pedido y devuelve la carga de su respuesta, salteando los eventos que lleguen antes.</summary>
    public async Task<JsonObject> AskAsync(string id, string requestType, object? data = null)
    {
        await SendAsync(ObsMessages.Request(id, requestType, data));
        while (true)
        {
            var message = await ReadAsync() ?? throw new InvalidOperationException("El servidor cerró antes de contestar");
            if ((int)message["op"]! == ObsMessages.OpRequestResponse)
            {
                return message["d"]!.AsObject();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
