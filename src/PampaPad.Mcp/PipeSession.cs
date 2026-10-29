using System.Text;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Mcp;

/// <summary>
/// Una conexión ya abierta con el programa, para lo que dura una llamada. Las respuestas se reconocen por su id; los eventos que llegan en el
/// medio (action.finished y script.output, que pueden adelantarse a la respuesta) van a quien los pida o se descartan.
/// </summary>
public sealed class PipeSession
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly Stream _pipe;
    private readonly StreamReader _reader;
    private readonly CancellationTokenSource _limit;
    private long _lastId;

    internal PipeSession(Stream pipe, CancellationTokenSource limit)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, Utf8);
        _limit = limit;
    }

    /// <summary>El tope de espera que rige, para el mensaje de no_response.</summary>
    public TimeSpan Wait { get; private set; }

    /// <summary>Cambia el tope desde ahora: script_test lo alarga cuando sabe el timeoutSec del script.</summary>
    public void WaitUpTo(TimeSpan wait)
    {
        Wait = wait;
        _limit.CancelAfter(wait);
    }

    /// <summary>Pide y devuelve la respuesta con su id. onEvent recibe los eventos que lleguen antes; sin él, se descartan.</summary>
    public async Task<string> SendAsync(string request, object data, Action<JsonElement>? onEvent, CancellationToken ct)
    {
        long id = ++_lastId;
        string line = Envelope.Request(id, request, data);

        // El programa corta la conexión con una línea más larga: se avisa acá, con un motivo que se entiende.
        if (Utf8.GetByteCount(line) > PipeProtocol.MaxLine)
        {
            return PipeClient.Refusal(ErrorCodes.InvalidData,
                $"El pedido pasa los {PipeProtocol.MaxLine / (1024 * 1024)} MB que acepta la tubería de PampaPad: mandalo en partes más chicas.");
        }

        await WriteLineAsync(line, ct).ConfigureAwait(false);
        while (true)
        {
            string reply = await ReadLineAsync(ct).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(reply);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("event", out _))
            {
                onEvent?.Invoke(root.Clone());
            }
            else if (root.TryGetProperty("id", out JsonElement got) && got.TryGetInt64(out long number) && number == id)
            {
                return reply;
            }
        }
    }

    /// <summary>El próximo evento que llegue.</summary>
    public async Task<JsonElement> NextEventAsync(CancellationToken ct)
    {
        while (true)
        {
            using JsonDocument document = JsonDocument.Parse(await ReadLineAsync(ct).ConfigureAwait(false));
            if (document.RootElement.TryGetProperty("event", out _))
            {
                return document.RootElement.Clone();
            }
        }
    }

    /// <summary>null si el programa contestó el saludo; si no, el error que se le devuelve a Claude.</summary>
    internal async Task<string?> HelloAsync(CancellationToken ct)
    {
        await WriteLineAsync(PipeProtocol.ClientHello(), ct).ConfigureAwait(false);
        string reply = await ReadLineAsync(ct).ConfigureAwait(false);
        return PipeProtocol.ReadHello(reply) switch
        {
            PipeProtocol.ProtocolVersion => null,
            int other => PipeClient.Refusal(ErrorCodes.InvalidData,
                $"PampaPad habla el protocolo {other} de la tubería y este MCP el {PipeProtocol.ProtocolVersion}: actualizá PampaPad y reconectá el MCP desde /mcp."),
            // El programa contesta un protocolo distinto con su error y cierra: ese error es lo que tiene que leer Claude.
            null => reply,
        };
    }

    private async Task WriteLineAsync(string line, CancellationToken ct)
    {
        await _pipe.WriteAsync(Utf8.GetBytes(line + "\n"), ct).ConfigureAwait(false);
        await _pipe.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task<string> ReadLineAsync(CancellationToken ct) =>
        await _reader.ReadLineAsync(ct).ConfigureAwait(false) ?? throw new EndOfStreamException("PampaPad cerró la tubería.");
}
