using System.Text;

namespace PampaPad.Core;

/// <summary>La mitad de SerialConsole que escribe por el cable: una línea por llamada, entera, y el resultado de esa línea (etapa 8b).</summary>
public sealed partial class SerialConsole
{
    // Un candado y no el canal de 01 §3.1: cada paso espera el resultado de su línea, y una cola lo obligaría a llevar una promesa por
    // línea. Alcanza para que dos acciones a la vez no mezclen sus bytes (plan de la 8b, decisión 8).
    private readonly SemaphoreSlim _sending = new(1, 1);

    // El flujo cuya escritura vencida ya se anotó; lo toca sólo quien tiene _sending.
    private Stream? _stuckLogged;

    /// <summary>
    /// Escribe la línea con su fin de línea. Termina bien al escribirla: del otro lado nadie acusa (02 §5). Sin flujo, soltada o con la
    /// escritura rota da console_disconnected, y la lectura sigue por su cuenta: si se desenchufó, la que se entera y reconecta es ella.
    /// </summary>
    public async Task<Result> SendAsync(string line, CancellationToken ct)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\n");
        await _sending.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Stream? stream;
            bool released;
            lock (_gate)
            {
                stream = _stream;
                released = _released;
            }

            if (released)
            {
                return Disconnected("La consola está soltada.");
            }

            if (stream is null)
            {
                return Disconnected("La consola no está conectada.");
            }

            try
            {
                // Write sincrónico y no WriteAsync: el WriteAsync de SerialStream pone WriteTimeout en infinito, y una escritura trabada
                // colgaba el paso con el candado tomado (revisión de la tanda 1 de la 8b).
                await Task.Run(
                    () =>
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush();
                    },
                    ct).ConfigureAwait(false);
                _stuckLogged = null;
                return Result.Success;
            }
            catch (TimeoutException e)
            {
                // Vencida con el puerto abierto: la lectura no se entera, así que la anota quien escribe, una vez por conexión
                // (el receptor 0.1.0 dejaba de aceptar bytes y en 11 minutos el registro no dijo nada, repaso de la 1.4.0).
                if (!ReferenceEquals(_stuckLogged, stream))
                {
                    _stuckLogged = stream;
                    _log.Warning(SourceId, $"La consola no acepta lo que escribe la PC: {e.Message} Se sigue intentando sin volver a anotarlo.");
                }

                return Disconnected($"No se pudo escribir en la consola: {e.Message}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException
                                      || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                // Desenchufada (IOException, o el error 5 de Windows como UnauthorizedAccessException: SerialConsole.cs:229-233) o cerrada
                // por el bucle o por Release mientras escribía (ObjectDisposedException). Una cancelación que no pidió el paso es el puerto que abortó la escritura.
                return Disconnected($"No se pudo escribir en la consola: {e.Message}");
            }
        }
        finally
        {
            _sending.Release();
        }
    }

    private static Result Disconnected(string message) => Result.Fail(ErrorCodes.ConsoleDisconnected, message);
}
