namespace PampaPad.Tests.Doubles;

/// <summary>
/// El reloj del OBS falso. Por defecto es el real; una prueba que le pone un FakeTimeProvider decide cuándo llegan los eventos con
/// demora (STARTED, STOPPED, el cambio de escena): así no depende de lo rápido que ande la máquina (hallazgo E4-6: en la de GitHub
/// la espera de 300 ms reales frenó la publicación de la v0.3.1).
/// </summary>
public sealed partial class FakeObs
{
    private int _waiting;

    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>El próximo StartRecord contesta 100 y no arranca, como el que llegó ~30 ms después de un STOPPED (hallazgo E4-11).</summary>
    public bool IgnoreNextStart { get; set; }

    /// <summary>Cuántos eventos con demora están esperando su hora. Sube recién con el temporizador creado: avanzar el reloj los suelta.</summary>
    public int Waiting => Volatile.Read(ref _waiting);

    private async Task DelayAsync(TimeSpan after)
    {
        var due = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using ITimer timer = Clock.CreateTimer(_ => due.TrySetResult(), null, after, Timeout.InfiniteTimeSpan);
        Interlocked.Increment(ref _waiting);
        try
        {
            await due.Task.WaitAsync(_stop.Token);
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }
    }
}
