using System.IO.Pipes;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Mcp;

/// <summary>
/// El lado de PampaPad.Mcp de la tubería (plan de la etapa MCP, decisiones 4 a 7). Una conexión por llamada: abre, saluda, pide, espera lo
/// suyo y cierra; después de una actualización no queda nada colgado. Lo que pase del otro lado vuelve como un sobre de error que Claude
/// lee, nunca como excepción (05 §7): sin programa, app_not_running.
/// </summary>
public sealed class PipeClient(string pipeName)
{
    /// <summary>Lo que espera un pedido común (plan de la etapa MCP, "Lo que puede morder").</summary>
    public static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(30);

    // Con el programa abierto la tubería atiende enseguida: más que esto es que no está.
    private const int ConnectTimeoutMs = 1000;

    /// <summary>Un pedido y su respuesta, tal como la escribió el programa.</summary>
    public Task<string> CallAsync(string request, object data, TimeSpan timeout, CancellationToken ct) =>
        RunAsync(timeout, (session, token) => session.SendAsync(request, data, null, token), ct);

    /// <summary>Abre la conexión, saluda y le pasa la sesión a work, con timeout como tope de espera (la sesión lo puede cambiar).</summary>
    public async Task<string> RunAsync(TimeSpan timeout, Func<PipeSession, CancellationToken, Task<string>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using (pipe.ConfigureAwait(false))
        {
            var session = new PipeSession(pipe, limit);
            session.WaitUpTo(timeout);
            try
            {
                await pipe.ConnectAsync(ConnectTimeoutMs, limit.Token).ConfigureAwait(false);
                return await session.HelloAsync(limit.Token).ConfigureAwait(false)
                    ?? await work(session, limit.Token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return Refusal(ErrorCodes.AppNotRunning, "PampaPad no está abierto");
            }
            catch (UnauthorizedAccessException)
            {
                // CurrentUserOnly compara cuenta y elevación: uno de los dos corre como administrador y el otro no.
                return Refusal(ErrorCodes.AppNotRunning,
                    "PampaPad corre con otros permisos: uno de los dos, Claude Code o PampaPad, está abierto como administrador. Abrilos con los mismos.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Refusal(ErrorCodes.NoResponse, $"PampaPad no contestó en {session.Wait.TotalSeconds:0} s.");
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                // El programa se cerró a mitad del pedido, o cortó una línea de más de MaxLine.
                return Refusal(ErrorCodes.NoResponse,
                    $"PampaPad cortó la conexión sin contestar: se cerró, o el pedido pasó los {PipeProtocol.MaxLine / (1024 * 1024)} MB por línea de la tubería.");
            }
        }
    }

    /// <summary>Un sobre de error como los del programa, para lo que se decide de este lado.</summary>
    public static string Refusal(string code, string message) => Envelope.Error(0, new Failure(code, message));
}
