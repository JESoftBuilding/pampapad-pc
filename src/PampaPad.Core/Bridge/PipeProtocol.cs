using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// Lo que comparten la tubería del programa y PampaPad.Mcp (plan de la etapa MCP, decisiones 4 y 7): el nombre, el saludo y los topes.
/// Después del saludo van los mismos sobres que la ventana (Envelope), una línea UTF-8 por mensaje, terminada en \n.
/// </summary>
public static class PipeProtocol
{
    public const int ProtocolVersion = 1;

    /// <summary>Sobra para un script de varios archivos de 256 KB. Una línea más larga cierra la conexión sin llegar al puente.</summary>
    public const int MaxLine = 4 * 1024 * 1024;

    public static TimeSpan HelloTimeout { get; } = TimeSpan.FromSeconds(5);

    /// <summary>El de la copia, como la instancia única: la de desarrollo no choca con la instalada (05 §10).</summary>
    public static string Name(bool dev) => dev ? "PampaPad-dev" : "PampaPad";

    /// <summary>El primer mensaje del cliente: {"hello":{"protocol":1}}.</summary>
    public static string ClientHello() => JsonSerializer.Serialize(new HelloEnvelope(new Hello(ProtocolVersion, null)), Json.Compact);

    /// <summary>La respuesta del programa: {"hello":{"protocol":1,"version":"0.8.0"}}.</summary>
    public static string ServerHello(string version) => JsonSerializer.Serialize(new HelloEnvelope(new Hello(ProtocolVersion, version)), Json.Compact);

    /// <summary>El protocolo que pide un saludo, o null si la línea no es un saludo.</summary>
    public static int? ReadHello(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("hello", out JsonElement hello)
                && hello.ValueKind == JsonValueKind.Object
                && hello.TryGetProperty("protocol", out JsonElement protocol)
                && protocol.ValueKind == JsonValueKind.Number
                && protocol.TryGetInt32(out int number)
                ? number
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record HelloEnvelope(Hello Hello);

    private sealed record Hello(int Protocol, string? Version);
}
