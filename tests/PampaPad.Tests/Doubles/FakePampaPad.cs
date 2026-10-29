using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// Un PampaPad de mentira del otro lado de la tubería, para probar PampaPad.Mcp sin el programa (E-MCP-8). Saluda como el de verdad, anota
/// cada pedido y contesta lo que diga Reply: una o varias líneas, que pueden ser eventos antes de la respuesta. Nombre único por prueba.
/// </summary>
public sealed class FakePampaPad : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly CancellationTokenSource _life = new();
    private readonly Task _serving;

    public FakePampaPad()
    {
        _serving = ServeAsync();
    }

    public string Name { get; } = "PampaPad-test-" + Guid.NewGuid().ToString("N");

    /// <summary>Cada pedido que llegó, en orden, ya abierto.</summary>
    public ConcurrentQueue<JsonElement> Requests { get; } = new();

    /// <summary>Lo que se contesta a un pedido. Por defecto, ok con {"answered":"&lt;nombre del pedido&gt;"}; null corta la conexión.</summary>
    public Func<JsonElement, IEnumerable<string>?> Reply { get; set; } = request =>
        [Ok(request, $$"""{"answered":"{{request.GetProperty("request").GetString()}}"}""")];

    public static string Ok(JsonElement request, string data) =>
        "{\"id\":" + request.GetProperty("id").GetInt64() + ",\"ok\":true,\"data\":" + data + "}";

    public static string Error(JsonElement request, string code, string message) =>
        "{\"id\":" + request.GetProperty("id").GetInt64() + ",\"ok\":false,\"error\":{\"code\":\"" + code + "\",\"message\":\"" + message + "\"}}";

    public JsonElement Single(string request) => Assert.Single(Requests, r => r.GetProperty("request").GetString() == request);

    public async ValueTask DisposeAsync()
    {
        await _life.CancelAsync();
        try
        {
            await _serving;
        }
        catch (OperationCanceledException)
        {
        }

        _life.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_life.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(_life.Token);
            _ = AnswerAsync(pipe);
        }
    }

    private async Task AnswerAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Utf8);
                var hello = await reader.ReadLineAsync(_life.Token);
                if (PipeProtocol.ReadHello(hello ?? string.Empty) != PipeProtocol.ProtocolVersion)
                {
                    return;
                }

                await WriteAsync(pipe, PipeProtocol.ServerHello("0.8.0"));
                while (await reader.ReadLineAsync(_life.Token) is { } line)
                {
                    using var document = JsonDocument.Parse(line);
                    var request = document.RootElement.Clone();
                    Requests.Enqueue(request);
                    if (Reply(request) is not { } replies)
                    {
                        return;
                    }

                    foreach (var reply in replies)
                    {
                        await WriteAsync(pipe, reply);
                    }
                }
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
            {
            }
        }
    }

    private async Task WriteAsync(Stream pipe, string line)
    {
        await pipe.WriteAsync(Utf8.GetBytes(line + "\n"), _life.Token);
        await pipe.FlushAsync(_life.Token);
    }
}
