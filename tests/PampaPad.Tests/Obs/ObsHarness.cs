using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

// Arma un ObsClient con reloj falso, estado y registro propios, y lo apaga al terminar la prueba.
internal sealed class ObsHarness : IAsyncDisposable
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);
    private readonly CancellationTokenSource _life = new();
    private readonly List<ObsLinkState> _links = [];
    private readonly TempFolder _temp = new();       // la carpeta del registro de esta prueba
    private int _stopped;

    public ObsHarness(Uri address, string? password = null)
    {
        Address = address;
        Password = password;
        Log = TestLogs.New(_temp);
        Client = new ObsClient(() => Address, () => Password, State, Log, Clock, () => ObsRunning);
        State.Subscribe((before, after) =>
        {
            if (before.Obs.Link == after.Obs.Link)
            {
                return;
            }

            lock (_links)
            {
                _links.Add(after.Obs.Link);
            }
        });
    }

    public FakeTimeProvider Clock { get; } = new();

    public StateStore State { get; } = new();

    public Log Log { get; }

    public ObsClient Client { get; }

    /// <summary>Lo que da la función de dirección en cada intento.</summary>
    public Uri Address { get; set; }

    /// <summary>Lo que da la función de clave en cada intento.</summary>
    public string? Password { get; set; }

    /// <summary>Lo que contesta "¿hay proceso obs64?".</summary>
    public bool ObsRunning { get; set; }

    public Task Loop { get; private set; } = Task.CompletedTask;

    public ObsLinkState Link => State.Current.Obs.Link;

    public static async Task<ObsHarness> ReadyAsync(FakeObs fake, string? password = null, bool obsRunning = false)
    {
        var harness = new ObsHarness(fake.Address, password) { ObsRunning = obsRunning };
        harness.Start();
        await harness.UntilLinkAsync(ObsLinkState.Ready);
        return harness;
    }

    public void Start() => Loop = Task.Run(() => Client.KeepConnectedAsync(_life.Token));

    public Task UntilLinkAsync(ObsLinkState link) => Eventually.UntilAsync(() => Link == link, $"enlace {link}");

    public Task AdvanceUntilLinkAsync(ObsLinkState link) => Eventually.AdvancingAsync(Clock, Step, () => Link == link, $"enlace {link}");

    // Los estados del enlace por los que pasó, en orden, según los avisos del StateStore.
    public List<ObsLinkState> LinksSeen()
    {
        lock (_links)
        {
            return [.. _links];
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        await _life.CancelAsync();
        await Loop.WaitAsync(TimeSpan.FromSeconds(10));
        await Client.DisposeAsync();
        await State.DisposeAsync();
        await Log.DisposeAsync();
        _temp.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
