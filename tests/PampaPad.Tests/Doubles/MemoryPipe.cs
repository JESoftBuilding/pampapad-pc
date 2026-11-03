using System.Text;
using System.Threading.Channels;

namespace PampaPad.Tests;

/// <summary>
/// El cable de la consola, en memoria. La prueba escribe lo que mandaría la placa y SerialConsole lo lee
/// como leería el puerto. Break imita desenchufar; cerrarlo corta la lectura pendiente, igual que el puerto real.
/// Lo que SerialConsole escribe queda en Written; Slow y FailsToWrite imitan un puerto lento y uno que se desenchufó.
/// </summary>
public sealed class MemoryPipe : Stream
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private byte[] _rest = [];
    private readonly StringBuilder _written = new();
    private volatile bool _closed;

    public bool Closed => _closed;

    /// <summary>
    /// Cerrar tira, como el puerto real con la placa desenchufada: SerialStream.Dispose falla en EscapeCommFunction con
    /// UnauthorizedAccessException, pero el runtime cierra el handle igual en su finally. Por eso primero se cierra
    /// (se corta la lectura pendiente) y recién después tira, y sólo la primera vez: cerrar de nuevo no hace nada.
    /// </summary>
    public bool FailsToClose { get; init; }

    /// <summary>
    /// Se corre en la primera vez que se cierra, con el cable ya cerrado y antes de tirar. Sirve para ordenar una carrera:
    /// que, mientras SerialConsole cierra el cable, alguien más toque el estado de la consola.
    /// </summary>
    public Action? OnFirstClose { get; init; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    /// <summary>Escribe de a un byte y cede entre uno y otro: dos escrituras sin candado se mezclarían.</summary>
    public bool Slow { get; init; }

    /// <summary>Mientras está prendido, escribir tira IOException, como el puerto con la placa desenchufada.</summary>
    public bool FailsToWrite { get; set; }

    /// <summary>Mientras tiene valor, escribir tira esta excepción: las otras que da un puerto serie al escribir.</summary>
    public Exception? WriteError { get; set; }

    /// <summary>
    /// El WriteAsync no termina nunca, ni cancelando, como el de SerialStream con el driver trabado: el asíncrono no respeta WriteTimeout
    /// (revisión de la tanda 1 de la 8b). El Write sincrónico anda.
    /// </summary>
    public bool AsyncWriteNeverEnds { get; init; }

    /// <summary>Lo que llegó a la placa, tal cual.</summary>
    public string Written
    {
        get
        {
            lock (_written)
            {
                return _written.ToString();
            }
        }
    }

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Lo que manda la placa. Puede ser media línea, o varias juntas.</summary>
    public void Feed(string text) => _incoming.Writer.TryWrite(Encoding.ASCII.GetBytes(text));

    /// <summary>Se desenchufó: lo que ya llegó se puede leer, y la lectura siguiente falla.</summary>
    public void Break() => _incoming.Writer.TryComplete();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_rest.Length == 0)
        {
            try
            {
                _rest = await _incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                throw new IOException("El dispositivo no está conectado.");
            }
        }

        var count = Math.Min(buffer.Length, _rest.Length);
        _rest.AsMemory(0, count).CopyTo(buffer);
        _rest = _rest[count..];
        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => WriteBytesAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (AsyncWriteNeverEnds)
        {
            await new TaskCompletionSource().Task.ConfigureAwait(false);
        }

        await WriteBytesAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteBytesAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (FailsToWrite)
        {
            throw new IOException("El dispositivo no está conectado.");
        }

        if (WriteError is { } error)
        {
            throw error;
        }

        foreach (var value in buffer.ToArray())
        {
            if (Slow)
            {
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }

            lock (_written)
            {
                _written.Append((char)value);
            }
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    protected override void Dispose(bool disposing)
    {
        var first = !_closed;
        _closed = true;
        _incoming.Writer.TryComplete();
        base.Dispose(disposing);
        if (!first)
        {
            return;
        }

        OnFirstClose?.Invoke();
        if (FailsToClose)
        {
            throw new UnauthorizedAccessException("Access to the path is denied.");
        }
    }
}
