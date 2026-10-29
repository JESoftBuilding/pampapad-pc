using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// Lo que la prueba de humo le pregunta al puente del programa real sobre el interruptor del MCP (revisión de la tanda 1 de la etapa MCP):
/// encendido por defecto, app.snapshot desde el MCP da ok; apagado desde Ajustes, el pedido siguiente da no_permission. La composición
/// arma Bridge.McpAllowed: cambiarlo por un valor fijo, o no asignarlo, dejaba todas las pruebas en verde.
/// </summary>
public static class McpSwitchCheck
{
    /// <summary>Null si el puente lee el interruptor en cada pedido; si no, qué pasó. Deja el MCP apagado en el local.json del puente.</summary>
    public static async Task<string?> ProblemAsync(Bridge bridge, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bridge);

        string on = await bridge.HandleAsync(Envelope.Request(1, RequestNames.AppSnapshot, new NoData()), Caller.Mcp, ct).ConfigureAwait(false);
        if (CodeOf(on) is not null)
        {
            return "Con el MCP encendido por defecto, app.snapshot desde el MCP no dio ok: " + Head(on);
        }

        string saved = await bridge.HandleAsync(
            Envelope.Request(2, RequestNames.SettingsSave, new SettingsSaveRequest(McpEnabled: false)), Caller.Window, ct).ConfigureAwait(false);
        if (CodeOf(saved) is not null)
        {
            return "settings.save con mcpEnabled: false no dio ok: " + Head(saved);
        }

        string off = await bridge.HandleAsync(Envelope.Request(3, RequestNames.AppSnapshot, new NoData()), Caller.Mcp, ct).ConfigureAwait(false);
        return CodeOf(off) == ErrorCodes.NoPermission ? null : "Con el MCP apagado en Ajustes, app.snapshot desde el MCP no dio no_permission: " + Head(off);
    }

    // La foto entera tapa el motivo en la salida de error del humo.
    private static string Head(string reply) => reply.Length <= 200 ? reply : reply[..200] + "…";

    // Null si la respuesta es ok; si no, el código del error.
    private static string? CodeOf(string reply)
    {
        using JsonDocument document = JsonDocument.Parse(reply);
        JsonElement root = document.RootElement;
        return root.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True
            ? null
            : root.TryGetProperty("error", out JsonElement error) && error.TryGetProperty("code", out JsonElement code) ? code.GetString() ?? "" : "";
    }
}
