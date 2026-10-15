using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Una SerialConsole sobre cables de memoria y con reloj falso. Cada intento de abrir el puerto saca lo que la prueba encoló.</summary>
internal sealed class SerialConsoleRig : IAsyncDisposable
{
    private readonly TempFolder _temp = new();
    private readonly Channel<InputEvent> _events = Channel.CreateUnbounded<InputEvent>();
    private readonly Queue<Func<Task<Stream>>> _opens = new();
    private int _openCalls;
    private int _eventCount;

    public SerialConsoleRig()
    {
        Log = new Log(_temp.Combine("log"), Clock);
        Store.Update(s => s with { Console = new ConsoleLink(ConsoleLinkState.Waiting, "COM7") });
        Source = new SerialConsole(Open, Store, Log, Clock);
        Source.Input += e =>
        {
            Interlocked.Increment(ref _eventCount);
            _events.Writer.TryWrite(e);
        };
    }

    public FakeTimeProvider Clock { get; } = new();

    public StateStore Store { get; } = new();

    public Log Log { get; }

    public SerialConsole Source { get; }

    public int OpenCalls => Volatile.Read(ref _openCalls);

    public int EventCount => Volatile.Read(ref _eventCount);

    public ConsoleLinkState Link => Store.Current.Console.State;

    /// <summary>El próximo intento de abrir el puerto devuelve este cable.</summary>
    public MemoryPipe NextOpens()
    {
        var pipe = new MemoryPipe();
        lock (_opens)
        {
            _opens.Enqueue(() => Task.FromResult<Stream>(pipe));
        }

        return pipe;
    }

    /// <summary>El próximo intento de abrir el puerto devuelve este flujo, que no es un MemoryPipe.</summary>
    public void NextOpensStream(Stream stream)
    {
        lock (_opens)
        {
            _opens.Enqueue(() => Task.FromResult(stream));
        }
    }

    /// <summary>El próximo intento de abrir el puerto queda esperando hasta que la prueba lo complete, como un puerto lento.</summary>
    public TaskCompletionSource<Stream> NextOpensLater()
    {
        var gate = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_opens)
        {
            _opens.Enqueue(() => gate.Task);
        }

        return gate;
    }

    /// <summary>El próximo intento de abrir el puerto falla, como cuando lo tiene tomado otro programa.</summary>
    public void NextFails(string message)
    {
        lock (_opens)
        {
            _opens.Enqueue(() => throw new UnauthorizedAccessException(message));
        }
    }

    /// <summary>El próximo evento que sacó la consola. El tope es sólo para que una prueba rota no cuelgue el CI.</summary>
    public async Task<InputEvent> NextEvent()
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await _events.Reader.ReadAsync(limit.Token);
    }

    /// <summary>Adelanta el reloj falso de a 100 ms hasta que pase lo que se espera.</summary>
    public Task AdvanceUntil(Func<bool> done) => Poll.Until(() =>
    {
        if (done())
        {
            return true;
        }

        Clock.Advance(TimeSpan.FromMilliseconds(100));
        return false;
    });

    public async ValueTask DisposeAsync()
    {
        await Source.DisposeAsync();
        await Store.DisposeAsync();
        await Log.DisposeAsync();
        _temp.Dispose();
    }

    private Task<Stream> Open(CancellationToken ct)
    {
        Interlocked.Increment(ref _openCalls);
        Func<Task<Stream>> next;
        lock (_opens)
        {
            next = _opens.Count > 0 ? _opens.Dequeue() : () => throw new IOException("El puerto no existe.");
        }

        return next();
    }
}
