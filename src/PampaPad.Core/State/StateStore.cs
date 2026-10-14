using System.Threading.Channels;

namespace PampaPad.Core;

/// <summary>El dueño del estado: cambia bajo un lock, numera y avisa fuera del lock, en orden (01 §4.4).</summary>
public sealed class StateStore : IAsyncDisposable
{
    private readonly Log? _log;
    private readonly Lock _gate = new();
    private readonly Channel<Change> _changes = Channel.CreateUnbounded<Change>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _dispatcher;
    private Action<AppState, AppState>[] _subscribers = [];
    private AppState _current = AppState.Initial;

    public StateStore(Log? log = null)
    {
        _log = log;
        _dispatcher = Task.Run(DispatchAsync);
    }

    public AppState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Aplica el cambio. Si no cambia nada, no numera ni avisa.</summary>
    public void Update(Func<AppState, AppState> change)
    {
        lock (_gate)
        {
            var previous = _current;
            var next = change(previous);
            if (next == previous)
            {
                return;
            }

            next = next with { Seq = previous.Seq + 1 };
            _current = next;
            _changes.Writer.TryWrite(new Change(previous, next));
        }
    }

    /// <summary>El suscriptor recibe (anterior, nuevo) en el hilo del despachador y sólo tiene que encolar.</summary>
    public IDisposable Subscribe(Action<AppState, AppState> onChange)
    {
        lock (_gate)
        {
            _subscribers = [.. _subscribers, onChange];
        }

        return new Subscription(this, onChange);
    }

    /// <summary>Termina cuando se entregaron todos los avisos pendientes.</summary>
    public async ValueTask DisposeAsync()
    {
        _changes.Writer.TryComplete();
        await _dispatcher.ConfigureAwait(false);
    }

    private async Task DispatchAsync()
    {
        await foreach (var change in _changes.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            foreach (var subscriber in Volatile.Read(ref _subscribers))
            {
                try
                {
                    subscriber(change.Previous, change.Current);
                }
                catch (Exception ex)
                {
                    _log?.Error("state", "Un suscriptor del estado tiró: " + ex.Message, ErrorCodes.InternalError);
                }
            }
        }
    }

    private void Unsubscribe(Action<AppState, AppState> onChange)
    {
        lock (_gate)
        {
            _subscribers = [.. _subscribers.Where(subscriber => subscriber != onChange)];
        }
    }

    private readonly record struct Change(AppState Previous, AppState Current);

    private sealed class Subscription(StateStore store, Action<AppState, AppState> onChange) : IDisposable
    {
        public void Dispose() => store.Unsubscribe(onChange);
    }
}
