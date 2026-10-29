using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;

namespace PampaPad.Core;

/// <summary>
/// La tubería con nombre por la que entra PampaPad.Mcp (01 §3.1; plan de la etapa MCP, decisiones 4 a 6). Sólo el usuario actual, varias
/// conexiones a la vez, cada una con su tarea: una rota no frena a las otras. Todo lo que entra va al puente como Caller.Mcp; quien
/// se conecta no puede elegir otro llamador. Por la conexión vuelven las respuestas y dos eventos, action.finished y script.output.
/// </summary>
public sealed class PipeListener(Bridge bridge, Log log, string pipeName, string version, TimeSpan? helloTimeout = null)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // Envelope.Event escribe "event" primero: el prefijo alcanza para elegir, sin abrir el JSON de cada evento.
    private static readonly string[] Forwarded =
        [$"{{\"event\":\"{EventNames.ActionFinished}\"", $"{{\"event\":\"{EventNames.ScriptOutput}\""];

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly TimeSpan _helloTimeout = helloTimeout ?? PipeProtocol.HelloTimeout;
    private readonly Lock _gate = new();
    private readonly List<Task> _connections = [];

    /// <summary>Atiende hasta que se cancela ct; entonces espera a que cierren las conexiones abiertas y vuelve sin tirar.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            bool failing = false;
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(
                        pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    if (pipe is not null)
                    {
                        await pipe.DisposeAsync().ConfigureAwait(false);
                    }

                    // Un cliente que se fue antes de que lo atendieran: se espera al siguiente.
                    if (pipe is not null && e is IOException)
                    {
                        continue;
                    }

                    // Lo demás, sobre todo no poder crear la tubería, se anota una vez y se reintenta al segundo: un listener muerto deja a
                    // Claude con app_not_running y el programa abierto (revisión de la tanda 1 de la etapa MCP).
                    if (!failing)
                    {
                        log.Warning("mcp", $"La tubería {pipeName} no pudo atender: {e.Message} Se reintenta cada segundo.", ErrorCodes.InternalError);
                        failing = true;
                    }

                    await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
                    continue;
                }
                catch (OperationCanceledException)
                {
                    if (pipe is not null)
                    {
                        await pipe.DisposeAsync().ConfigureAwait(false);
                    }

                    throw;
                }

                failing = false;
                lock (_gate)
                {
                    _connections.RemoveAll(task => task.IsCompleted);
                    _connections.Add(ServeAsync(pipe, ct));
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            // Sin tubería el programa sigue: las teclas y la ventana no dependen de ella. Claude ve app_not_running.
            log.Error("mcp", $"La tubería {pipeName} dejó de atender: {e}", ErrorCodes.InternalError);
        }

        Task[] open;
        lock (_gate)
        {
            open = [.. _connections];
        }

        await Task.WhenAll(open).ConfigureAwait(false);
    }

    // Nunca tira: cada conexión termina sola, y lo que falla queda en el registro.
    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await Task.Yield();
        using var life = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // shortcut: sin tope, un cliente que no lee acumula eventos en memoria; con una conexión por llamada (decisión 6) no pasa.
        var outgoing = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        Task writer = WriteLoopAsync(pipe, outgoing.Reader, life);
        IDisposable? events = null;
        try
        {
            var reader = new LineReader(pipe, PipeProtocol.MaxLine);
            string? hello = await ReadHelloAsync(reader, life.Token).ConfigureAwait(false);
            if (hello is null)
            {
                return;
            }

            if (PipeProtocol.ReadHello(hello) != PipeProtocol.ProtocolVersion)
            {
                log.Warning("mcp", "Un cliente de la tubería saludó con otro protocolo: se cerró la conexión", ErrorCodes.InvalidData);
                outgoing.Writer.TryWrite(Envelope.Error(0, new Failure(
                    ErrorCodes.InvalidData, $"Esta versión de PampaPad habla el protocolo {PipeProtocol.ProtocolVersion} de la tubería: actualizá PampaPad o el MCP.")));
                return;
            }

            outgoing.Writer.TryWrite(PipeProtocol.ServerHello(version));
            events = bridge.Subscribe(json =>
            {
                // Apagado, la tubería sigue saludando y negando pedidos, pero no deja salir nada.
                if (bridge.McpAllowed() && Array.Exists(Forwarded, prefix => json.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    outgoing.Writer.TryWrite(json);
                }
            });

            while (await reader.ReadAsync(life.Token).ConfigureAwait(false) is { } line)
            {
                // Caller.Mcp fijo: lo que diga el mensaje no cambia quién pide (01 §5).
                outgoing.Writer.TryWrite(await bridge.HandleAsync(line, Caller.Mcp, life.Token).ConfigureAwait(false));
            }
        }
        catch (InvalidDataException)
        {
            log.Warning("mcp", $"Una línea de más de {PipeProtocol.MaxLine / (1024 * 1024)} MB por la tubería: se cerró la conexión", ErrorCodes.InvalidData);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Se cerró el programa o el cliente se fue: no hay a quién contestarle.
        }
        catch (Exception e)
        {
            log.Error("mcp", $"Una conexión de la tubería reventó: {e}", ErrorCodes.InternalError);
        }
        finally
        {
            events?.Dispose();
            outgoing.Writer.TryComplete();
            await writer.ConfigureAwait(false);
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<string?> ReadHelloAsync(LineReader reader, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_helloTimeout);
        try
        {
            return await reader.ReadAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.Warning("mcp", $"Un cliente de la tubería no saludó en {_helloTimeout.TotalSeconds:0.#} s: se cerró la conexión", ErrorCodes.InvalidData);
            return null;
        }
    }

    // Un solo escritor por conexión: las respuestas y los eventos no se mezclan a mitad de línea. Si el cliente se fue, corta la lectura.
    private static async Task WriteLoopAsync(Stream pipe, ChannelReader<string> outgoing, CancellationTokenSource life)
    {
        try
        {
            await foreach (string message in outgoing.ReadAllAsync(life.Token).ConfigureAwait(false))
            {
                await pipe.WriteAsync(Utf8.GetBytes(message + "\n"), life.Token).ConfigureAwait(false);
                await pipe.FlushAsync(life.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            await life.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Lee líneas de a una sin pasarse del tope: una más larga tira InvalidDataException. null es que el cliente cerró.</summary>
    private sealed class LineReader(Stream stream, int max)
    {
        private readonly byte[] _buffer = new byte[64 * 1024];
        private readonly MemoryStream _line = new();
        private int _start;
        private int _end;

        public async Task<string?> ReadAsync(CancellationToken ct)
        {
            _line.SetLength(0);
            while (true)
            {
                if (_start < _end)
                {
                    int newline = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
                    int take = (newline < 0 ? _end : newline) - _start;
                    if (_line.Length + take > max)
                    {
                        throw new InvalidDataException("La línea se pasa del tope.");
                    }

                    _line.Write(_buffer, _start, take);
                    _start += take;
                    if (newline >= 0)
                    {
                        _start++;
                        return Utf8.GetString(_line.GetBuffer(), 0, (int)_line.Length).TrimEnd('\r');
                    }
                }

                _start = 0;
                _end = await stream.ReadAsync(_buffer, ct).ConfigureAwait(false);
                if (_end == 0)
                {
                    return null;
                }
            }
        }
    }
}
