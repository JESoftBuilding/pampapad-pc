using PampaPad.Core;
using PampaPad.Tests.Doubles;

namespace PampaPad.Tests;

/// <summary>Un OBS falso con un ObsClient de verdad conectado. Lo usan las pruebas de obs.marker y la de punta a punta.</summary>
internal sealed class ObsSession : IAsyncDisposable
{
    private readonly TempFolder _temp = new();
    private readonly CancellationTokenSource _life = new();
    private Task _loop = Task.CompletedTask;

    private ObsSession(FakeObs obs)
    {
        Obs = obs;
        Log = new Log(_temp.Combine("log"), TimeProvider.System);
        Client = new ObsClient(() => obs.Address, () => null, State, Log, TimeProvider.System);
    }

    public FakeObs Obs { get; }

    public StateStore State { get; } = new();

    public Log Log { get; }

    public ObsClient Client { get; }

    /// <summary>Arranca el OBS falso en ese estado de grabación (detenido, grabando o en pausa) y espera a que el cliente lo haya leído.</summary>
    public static async Task<ObsSession> StartAsync(RecordingState recording = RecordingState.Stopped, Action<FakeObs>? before = null)
    {
        FakeObs obs = FakeObs.Start();
        before?.Invoke(obs);
        if (recording is RecordingState.Recording or RecordingState.Paused)
        {
            await obs.StartRecordingAsync();
        }

        if (recording == RecordingState.Paused)
        {
            await obs.PauseRecordingAsync();
        }

        var session = new ObsSession(obs);
        session._loop = Task.Run(() => session.Client.KeepConnectedAsync(session._life.Token));
        await Poll.Until(() => session.State.Current.Obs is { Link: ObsLinkState.Ready } known && known.Recording == recording);
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        await _life.CancelAsync();
        await _loop.WaitAsync(TimeSpan.FromSeconds(10));
        await Client.DisposeAsync();
        await Obs.DisposeAsync();
        await State.DisposeAsync();
        await Log.DisposeAsync();
        _life.Dispose();
        _temp.Dispose();
    }
}

internal static class Poll
{
    /// <summary>Espera a que algo que pasa en otro hilo termine de pasar. Sólo para pruebas con enlaces o tareas reales.</summary>
    public static async Task Until(Func<bool> condition, int milliseconds = 5000)
    {
        for (int waited = 0; !condition(); waited += 10)
        {
            if (waited >= milliseconds)
            {
                throw new TimeoutException("La condición no se cumplió a tiempo.");
            }

            await Task.Delay(10);
        }
    }
}
