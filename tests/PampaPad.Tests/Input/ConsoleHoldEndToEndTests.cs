using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>De la línea de la placa al mantenido, con el reloj de los gestos andando solo y los estados llegando cada 50 ms como en la placa.</summary>
public class ConsoleHoldEndToEndTests
{
    private const string Key5Down = "#301110EAAAAAAAAAA=\n";
    private const string Key12Down = "#301110AAgAAAAAAAA=\n";
    private const string AllUp = "#301110AAAAAAAAAAA=\n";

    [Fact]
    public async Task A_key_held_on_the_board_fires_its_hold_once_and_letting_go_fires_nothing_more()
    {
        await using var rig = new KeysRig();
        var cable = new MemoryPipe();
        var seen = 0;
        await using var console = new SerialConsole(_ => Task.FromResult<Stream>(cable), rig.Store, rig.Log, rig.Clock);
        rig.Dispatcher.Attach(console);
        console.Input += _ => Interlocked.Increment(ref seen);
        await console.StartAsync(CancellationToken.None);
        using var life = new CancellationTokenSource();
        var ticking = rig.Dispatcher.KeepTickingAsync(rig.Clock, life.Token);

        cable.Feed(Key12Down);
        await Poll.Until(() => Volatile.Read(ref seen) == 1);
        for (var state = 2; state <= 21; state++)               // un segundo apretada, un estado cada 50 ms
        {
            var expected = state;
            rig.Clock.Advance(TimeSpan.FromMilliseconds(50));
            cable.Feed(Key12Down);
            await Poll.Until(() => Volatile.Read(ref seen) == expected);
        }

        Assert.Equal(("fin", Trigger.Key), await rig.NextRun());

        cable.Feed(AllUp);                                      // soltar después del mantenido no dispara el toque
        rig.Clock.Advance(TimeSpan.FromMilliseconds(50));
        cable.Feed(Key5Down);                                   // centinela: otra tecla
        Assert.Equal(("short", Trigger.Key), await rig.NextRun());
        Assert.Equal(2, rig.Runs);

        await life.CancelAsync();
        await ticking;
    }

    [Fact]
    public async Task A_key_with_on_hold_tapped_on_the_board_fires_its_tap_action_on_release()
    {
        await using var rig = new KeysRig();
        var cable = new MemoryPipe();
        var seen = 0;
        await using var console = new SerialConsole(_ => Task.FromResult<Stream>(cable), rig.Store, rig.Log, rig.Clock);
        rig.Dispatcher.Attach(console);
        console.Input += _ => Interlocked.Increment(ref seen);
        await console.StartAsync(CancellationToken.None);

        cable.Feed(Key12Down);
        await Poll.Until(() => Volatile.Read(ref seen) == 1);
        Assert.Empty(rig.Store.Current.Running);                // apretada no dispara: espera a ver si la mantienen
        Assert.Equal(0, rig.Runs);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(100));
        cable.Feed(AllUp);

        Assert.Equal(("inicio", Trigger.Key), await rig.NextRun());
        Assert.Equal(1, rig.Runs);
    }
}
