using PampaPad.Core;

namespace PampaPad.Tests;

// Cuando cerrar el puerto tira. Con la placa desenchufada, SerialStream.Dispose falla con UnauthorizedAccessException (E3b-6, prueba
// manual del 2026-10-14) y antes de este arreglo el error se llevaba puesto el bucle: la consola quedaba "conectada" para siempre y al
// salir el programa se caía. MemoryPipe con FailsToClose es ese puerto; los demás dobles nunca tiraban al cerrar.
public class SerialConsoleCloseFailureTests
{
    private const string Key3Down = "#301110BAAAAAAAAAA=\n";
    private const string Key5Down = "#301110EAAAAAAAAAA=\n";
    private const string Key12Down = "#301110AAgAAAAAAAA=\n";

    [Fact]
    public async Task Unplugging_a_port_that_fails_to_close_waits_logs_once_and_reconnects_on_its_own()
    {
        await using var rig = new SerialConsoleRig();
        var first = new MemoryPipe { FailsToClose = true };
        rig.NextOpensStream(first);
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        first.Break();
        await Poll.Until(() => rig.Link == ConsoleLinkState.Waiting);

        Assert.True(first.Closed);
        var warnings = LostConsoleWarnings(rig);
        var warning = Assert.Single(warnings);
        Assert.Contains("El dispositivo no está conectado.", warning, StringComparison.Ordinal);   // el motivo es el de la lectura, no el del cierre
        Assert.DoesNotContain("Access to the path", warning, StringComparison.Ordinal);
        Assert.EndsWith("Se reintenta cada 2 s.", warning, StringComparison.Ordinal);

        rig.Clock.Advance(SerialConsole.RetryDelay);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        second.Feed(Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(2, rig.OpenCalls);
        Assert.Single(LostConsoleWarnings(rig));            // reconectar no anota nada más
    }

    [Fact]
    public async Task Unplugged_holding_a_key_with_a_port_that_fails_to_close_fires_nothing_extra()
    {
        await using var rig = new KeysRig();
        var first = new MemoryPipe { FailsToClose = true };
        var second = new MemoryPipe();
        var cables = new Queue<Stream>([first, second]);
        var seen = 0;
        await using var console = new SerialConsole(_ => Task.FromResult(cables.Dequeue()), rig.Store, rig.Log, rig.Clock);
        rig.Dispatcher.Attach(console);
        console.Input += _ => Interlocked.Increment(ref seen);
        await console.StartAsync(CancellationToken.None);

        first.Feed(Key12Down);                                  // la tecla con onHold, apretada
        await Poll.Until(() => Volatile.Read(ref seen) == 1);
        first.Break();                                          // se desenchufa y además cerrar el puerto tira
        await Poll.Until(() => rig.Store.Current.Console.State == ConsoleLinkState.Waiting);

        rig.Clock.Advance(Gestures.DefaultFreshness);           // 150 ms sin estado: se da por soltada
        rig.Dispatcher.Tick();
        rig.Clock.Advance(TimeSpan.FromSeconds(2));             // pasan el segundo de mantener y el reintento del puerto
        rig.Dispatcher.Tick();
        await Poll.Until(() => rig.Store.Current.Console.State == ConsoleLinkState.Connected);

        second.Feed(Key5Down);                                  // vuelve el cable y se aprieta otra tecla
        Assert.Equal(("short", Trigger.Key), await rig.NextRun());
        Assert.Equal(1, rig.Runs);                              // ni "inicio" ni "fin": el error de cierre no inventó ningún gesto
        await Poll.Until(() => Volatile.Read(ref seen) == 2);   // el Press de la tecla 12 y el de la 5, ningún "soltó" de más
    }

    [Fact]
    public async Task Releasing_a_port_that_fails_to_close_does_not_throw_reaches_released_and_taking_it_back_reopens_it()
    {
        await using var rig = new SerialConsoleRig();
        var first = new MemoryPipe { FailsToClose = true };
        rig.NextOpensStream(first);
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        rig.Source.Release(true);                               // si el error de cierre saliera, la prueba caería acá

        await Poll.Until(() => rig.Link == ConsoleLinkState.Released);
        Assert.True(first.Closed);
        Assert.Contains(rig.Log.Recent(), e => e.Module == SerialConsole.SourceId && e.Message == "Se soltó el puerto de la consola.");
        Assert.Empty(LostConsoleWarnings(rig));

        rig.Source.Release(false);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        second.Feed(Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(2, rig.OpenCalls);
    }

    [Fact]
    public async Task Disposing_while_connected_to_a_port_that_fails_to_close_does_not_throw()
    {
        await using var rig = new SerialConsoleRig();
        var port = new MemoryPipe { FailsToClose = true };
        rig.NextOpensStream(port);
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);

        await rig.Source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(port.Closed);
        Assert.DoesNotContain(rig.Log.Recent(), e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Disposing_after_an_unplug_whose_close_failed_does_not_throw()
    {
        await using var rig = new SerialConsoleRig();
        var port = new MemoryPipe { FailsToClose = true };
        rig.NextOpensStream(port);
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);
        port.Break();
        await Poll.Until(() => rig.Link == ConsoleLinkState.Waiting);

        await rig.Source.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_port_that_finishes_opening_after_Release_and_fails_to_close_does_not_count_as_lost_and_the_retake_opens_the_next_one()
    {
        await using var rig = new SerialConsoleRig();
        var slow = rig.NextOpensLater();
        var second = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        await Poll.Until(() => rig.OpenCalls == 1);
        rig.Source.Release(true);                               // se suelta mientras el puerto todavía está abriendo
        var late = new MemoryPipe { FailsToClose = true, OnFirstClose = () => rig.Source.Release(false) };   // y lo vuelven a tomar justo mientras se lo cierra

        slow.SetResult(late);

        await Poll.Until(() => rig.Link == ConsoleLinkState.Connected);     // si el error de cierre saliera, se anotaría "Sin consola" y esperaría 2 s sin que nadie adelante el reloj
        Assert.True(late.Closed);
        Assert.Equal(2, rig.OpenCalls);
        Assert.DoesNotContain(rig.Log.Recent(), e => e.Level >= LogLevel.Warning);
        second.Feed(Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
    }

    private static List<string> LostConsoleWarnings(SerialConsoleRig rig) =>
        rig.Log.Recent()
            .Where(e => e.Module == SerialConsole.SourceId && e.Level == LogLevel.Warning && e.Message.StartsWith("Sin consola:", StringComparison.Ordinal))
            .Select(e => e.Message)
            .ToList();

    private static InputEvent Press(int key) => new(SerialConsole.SourceId, $"key.{key}", Gesture.Press);
}
