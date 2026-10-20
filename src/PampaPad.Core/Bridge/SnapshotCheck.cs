using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// Lo que la prueba de humo le pregunta al puente del programa real (revisión de E5-23a): app.snapshot, pedido como lo pide la ventana,
/// tiene que traer los mismos tipos de paso y de condición que corre el Executor, y ninguna de las dos listas vacía. Contract.cs arma su
/// foto con Catalog.Build y Conditions.Build sin pasar por Composition: si Composition le pasara al puente otra lista, o [], ninguna prueba
/// lo vería y la página recibiría conditionTypes vacía, como antes de E5-23a. El orden no se compara: lo fijan las pruebas de cada catálogo.
/// </summary>
public static class SnapshotCheck
{
    /// <summary>Null si la foto trae lo que corre el programa; si no, qué manda y qué debería mandar.</summary>
    public static async Task<string?> ProblemAsync(
        Bridge bridge, IEnumerable<string> stepTypes, IEnumerable<string> conditionTypes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(stepTypes);
        ArgumentNullException.ThrowIfNull(conditionTypes);

        string reply = await bridge.HandleAsync(Envelope.Request(1, RequestNames.AppSnapshot, new NoData()), Caller.Window, ct)
            .ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(reply);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("ok", out JsonElement ok) || ok.ValueKind != JsonValueKind.True
            || !root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
        {
            return "app.snapshot no devolvió la foto: " + reply;
        }

        return Compare(data, "stepTypes", "tipos de paso", stepTypes) ?? Compare(data, "conditionTypes", "tipos de condición", conditionTypes);
    }

    private static string? Compare(JsonElement data, string key, string what, IEnumerable<string> running)
    {
        string[] sent = data.TryGetProperty(key, out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Select(IdOf).Order(StringComparer.Ordinal)]
            : [];
        string[] expected = [.. running.Order(StringComparer.Ordinal)];
        if (sent.Length > 0 && sent.SequenceEqual(expected, StringComparer.Ordinal))
        {
            return null;
        }

        return $"app.snapshot manda {key} con [{string.Join(", ", sent)}] y el programa corre los {what} [{string.Join(", ", expected)}].";
    }

    private static string IdOf(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()!
            : "(sin id)";
}
