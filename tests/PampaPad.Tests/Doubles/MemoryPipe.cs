using System.Text;
using System.Threading.Channels;

namespace PampaPad.Tests;

/// <summary>
/// El cable de la consola, en memoria. La prueba escribe lo que mandaría la placa y SerialConsole lo lee
/// como leería el puerto. Break imita desenchufar; cerrarlo corta la lectura pendiente, igual que el puerto real.
/// </summary>
public sealed class MemoryPipe : Stream
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private byte[] _rest = [];
    private volatile bool _closed;

    public bool Closed => _closed;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

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

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _closed = true;
        _incoming.Writer.TryComplete();
        base.Dispose(disposing);
    }
}
