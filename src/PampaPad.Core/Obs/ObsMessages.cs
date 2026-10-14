using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PampaPad.Core;

/// <summary>Un pedido para mandar: el tipo de obs-websocket y sus datos, que se serializan con <see cref="Json.Compact"/>.</summary>
public readonly record struct ObsRequest(string Type, object? Data = null);

/// <summary>
/// La respuesta de OBS a un pedido. <c>Code</c> 0 no llega nunca de OBS: quiere decir que no hubo respuesta,
/// y <c>Comment</c> trae entonces el código de <see cref="ErrorCodes"/> (obs_disconnected, obs_timeout o cancelled).
/// </summary>
public readonly record struct ObsResponse(bool Ok, int Code, string? Comment, JsonElement Data)
{
    public string? Error => Code == 0 ? Comment : null;

    public static ObsResponse Fail(string errorCode) => new(false, 0, errorCode, default);
}

public readonly record struct ObsEvent(string Type, int Intent, JsonElement Data);

public readonly record struct ObsHello(string ObsVersion, string WebSocketVersion, int RpcVersion, string? Challenge, string? Salt);

/// <summary>Un mensaje recibido: el código de operación y su carga, ya copiada (no depende del buffer de recepción).</summary>
public readonly record struct ObsIncoming(int Op, JsonElement D);

/// <summary>El único lugar que conoce la forma de los mensajes de obs-websocket v5 (spec 03 §2).</summary>
public static class ObsMessages
{
    public const int OpHello = 0;
    public const int OpIdentify = 1;
    public const int OpIdentified = 2;
    public const int OpEvent = 5;
    public const int OpRequest = 6;
    public const int OpRequestResponse = 7;
    public const int OpRequestBatch = 8;
    public const int OpRequestBatchResponse = 9;
    public const int RpcVersion = 1;
    public const int CloseAuthenticationFailed = 4009;
    public const int CloseSessionInvalidated = 4011;
    public const int StatusSuccess = 100;
    public const int StatusNotReady = 207;

    /// <summary>base64(SHA-256(base64(SHA-256(clave + salt)) + challenge)), como en relevar_obs.py:103-105.</summary>
    public static string Authentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    public static string Identify(int eventSubscriptions, string? authentication) => Write(OpIdentify, d =>
    {
        d.WriteNumber("rpcVersion", RpcVersion);
        d.WriteNumber("eventSubscriptions", eventSubscriptions);
        if (authentication is not null)
        {
            d.WriteString("authentication", authentication);
        }
    });

    public static string Request(string requestId, string requestType, object? data) => Write(OpRequest, d =>
    {
        d.WriteString("requestType", requestType);
        d.WriteString("requestId", requestId);
        WriteData(d, data);
    });

    public static string Batch(string requestId, IReadOnlyList<ObsRequest> requests) => Write(OpRequestBatch, d =>
    {
        d.WriteString("requestId", requestId);
        d.WriteStartArray("requests");
        foreach (var request in requests)
        {
            d.WriteStartObject();
            d.WriteString("requestType", request.Type);
            WriteData(d, request.Data);
            d.WriteEndObject();
        }

        d.WriteEndArray();
    });

    /// <summary>Falso si lo que llegó no es un mensaje de obs-websocket. Quien llama lo anota y sigue recibiendo.</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out ObsIncoming message)
    {
        message = default;
        try
        {
            var reader = new Utf8JsonReader(utf8);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (!Has(root, "op", out var op) || op.ValueKind != JsonValueKind.Number || !op.TryGetInt32(out var code))
            {
                return false;
            }

            message = new ObsIncoming(code, Has(root, "d", out var d) ? d.Clone() : default);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static ObsHello ToHello(JsonElement d)
    {
        var locked = Has(d, "authentication", out var authentication);
        return new ObsHello(
            Text(d, "obsStudioVersion") ?? "?",
            Text(d, "obsWebSocketVersion") ?? "?",
            (int)Number(d, "rpcVersion"),
            locked ? Text(authentication, "challenge") : null,
            locked ? Text(authentication, "salt") : null);
    }

    public static ObsEvent ToEvent(JsonElement d) =>
        new(Text(d, "eventType") ?? string.Empty, (int)Number(d, "eventIntent"), Has(d, "eventData", out var data) ? data : default);

    public static string? RequestId(JsonElement d) => Text(d, "requestId");

    public static ObsResponse ToResponse(JsonElement d)
    {
        if (!Has(d, "requestStatus", out var status))
        {
            return ObsResponse.Fail(ErrorCodes.ObsRequestFailed);
        }

        var data = Has(d, "responseData", out var found) ? found : default;
        return new ObsResponse(Flag(status, "result"), (int)Number(status, "code"), Text(status, "comment"), data);
    }

    public static IReadOnlyList<ObsResponse> ToBatch(JsonElement d) => [.. Items(d, "results").Select(ToResponse)];

    /// <summary>Si el GetVersion de este OBS trae el pedido en availableRequests (spec 03 §2, paso 4).</summary>
    public static bool Supports(ObsResponse getVersion, string requestType)
    {
        if (!getVersion.Ok)
        {
            return false;
        }

        foreach (var item in Items(getVersion.Data, "availableRequests"))
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() == requestType)
            {
                return true;
            }
        }

        return false;
    }

    // Lectores tolerantes: lo que falta o viene con otro tipo da el valor vacío, nunca una excepción.
    public static bool Has(JsonElement e, string name, out JsonElement value)
    {
        value = default;
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value);
    }

    public static string? Text(JsonElement e, string name) =>
        Has(e, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static bool Flag(JsonElement e, string name) => Has(e, name, out var value) && value.ValueKind == JsonValueKind.True;

    public static double Number(JsonElement e, string name) =>
        Has(e, name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    public static IReadOnlyList<JsonElement> Items(JsonElement e, string name)
    {
        if (!Has(e, name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. value.EnumerateArray()];
    }

    private static string Write(int op, Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("op", op);
            writer.WriteStartObject("d");
            body(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteData(Utf8JsonWriter writer, object? data)
    {
        if (data is null)
        {
            return;
        }

        writer.WritePropertyName("requestData");
        JsonSerializer.Serialize(writer, data, data.GetType(), Json.Compact);
    }
}
