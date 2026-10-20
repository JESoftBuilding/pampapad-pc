using System.Text.Json;

namespace PampaPad.Core;

/// <summary>Un pedido ya abierto: su id, el nombre del pedido y los datos todavía sin deserializar.</summary>
public sealed record IncomingRequest(long Id, string Request, JsonElement Data);

/// <summary>
/// Los cuatro sobres del puente, nada más (01 §5): pedido { id, request, data }, respuesta { id, ok, data },
/// error { id, ok: false, error { code, message, where } } y evento { event, seq, data }. Todos con Json.Compact.
/// </summary>
public static class Envelope
{
    private static readonly JsonElement EmptyData = CreateEmpty();

    public static Result<IncomingRequest> Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid("El pedido tiene que ser un objeto JSON.", null);
            }

            if (!root.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out long number))
            {
                return Invalid("El pedido no trae un id numérico.", "id");
            }

            if (!root.TryGetProperty("request", out JsonElement request) || request.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(request.GetString()))
            {
                return Invalid("El pedido no dice qué pide.", "request");
            }

            JsonElement data = root.TryGetProperty("data", out JsonElement given) && given.ValueKind != JsonValueKind.Null
                ? given.Clone()
                : EmptyData;
            return new IncomingRequest(number, request.GetString()!, data);
        }
        catch (JsonException e)
        {
            return Invalid("El pedido no es JSON válido: " + e.Message, null);
        }
    }

    /// <summary>El id de un pedido que no se pudo abrir, para contestarle con su error; 0 si no se puede rescatar.</summary>
    public static long IdOf(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("id", out JsonElement id)
                && id.ValueKind == JsonValueKind.Number
                && id.TryGetInt64(out long number)
                ? number
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    public static string Request(long id, string request, object data) =>
        JsonSerializer.Serialize(new RequestEnvelope(id, request, data), Json.Compact);

    public static string Response(long id, object data) =>
        JsonSerializer.Serialize(new ResponseEnvelope(id, true, data), Json.Compact);

    public static string Error(long id, Failure failure) =>
        JsonSerializer.Serialize(new ErrorEnvelope(id, false, failure), Json.Compact);

    public static string Event(string name, long seq, object data) =>
        JsonSerializer.Serialize(new EventEnvelope(name, seq, data), Json.Compact);

    private static Result<IncomingRequest> Invalid(string message, string? where) =>
        Result<IncomingRequest>.Fail(ErrorCodes.InvalidData, message, where);

    private static JsonElement CreateEmpty()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    // Data es object a propósito: el serializador usa el tipo real, que es el record de Dtos.cs de cada pedido.
    private sealed record RequestEnvelope(long Id, string Request, object Data);

    private sealed record ResponseEnvelope(long Id, bool Ok, object Data);

    private sealed record ErrorEnvelope(long Id, bool Ok, Failure Error);

    private sealed record EventEnvelope(string Event, long Seq, object Data);
}
