using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Un ObsClient sin OBS del otro lado: el puerto 9 de la PC, donde no escucha nadie. El enlace queda en closed.</summary>
internal sealed class OfflineObs : IAsyncDisposable
{
    private readonly TempFolder _temp = new();

    public OfflineObs()
    {
        Log = new Log(_temp.Combine("log"), TimeProvider.System);
        Client = new ObsClient(() => new Uri("ws://127.0.0.1:9"), () => null, State, Log, TimeProvider.System);
    }

    public StateStore State { get; } = new();

    public Log Log { get; }

    public ObsClient Client { get; }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await State.DisposeAsync();
        await Log.DisposeAsync();
        _temp.Dispose();
    }
}
