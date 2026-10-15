namespace PampaPad.Tests;

/// <summary>Un flujo que no tiene nada para leer y devuelve 0, como un puerto que se cerró del otro lado.</summary>
public sealed class EndedStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);

    public override int Read(byte[] buffer, int offset, int count) => 0;

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Un puerto como el real: la lectura pendiente no respeta el token y sólo termina, con un error, cuando alguien cierra el flujo
/// (01 §9). MemoryPipe sí respeta el token, así que no sirve para probar que cerrar el puerto es lo que corta la lectura.
/// </summary>
public sealed class DeafStream : Stream
{
    private readonly TaskCompletionSource<int> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(_read.Task);

    public override int Read(byte[] buffer, int offset, int count) => _read.Task.GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _closed = true;
        _read.TrySetException(new IOException("El puerto se cerró."));
        base.Dispose(disposing);
    }
}
