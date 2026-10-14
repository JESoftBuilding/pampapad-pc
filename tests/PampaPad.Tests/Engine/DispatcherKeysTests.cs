using PampaPad.Core;

namespace PampaPad.Tests;

public class DispatcherKeysTests
{
    [Theory]
    [InlineData(5, Gesture.Press, true, "marca-short")]         // sin onHold: al apretar, sin demora
    [InlineData(5, Gesture.Tap, true, null)]                    // ...y el toque que llega al soltar no repite
    [InlineData(5, Gesture.Hold, true, null)]
    [InlineData(5, Gesture.Release, true, null)]
    [InlineData(12, Gesture.Press, true, null)]                 // con onHold: apretar no dispara
    [InlineData(12, Gesture.Tap, true, "marca-inicio")]         // el toque, al soltar
    [InlineData(12, Gesture.Hold, true, "marca-fin")]           // el mantenido, al llegar al umbral
    [InlineData(12, Gesture.Release, true, null)]
    [InlineData(5, Gesture.Tap, false, "marca-short")]          // atajo de teclado: la acción del toque
    [InlineData(12, Gesture.Tap, false, "marca-inicio")]        // ...y nunca la de onHold
    [InlineData(12, Gesture.Hold, false, null)]
    public void The_gesture_table_picks_the_action(int position, Gesture gesture, bool viaGestures, string? expected)
    {
        using var temp = new TempFolder();
        var key = KeysRig.LoadLibrary(temp).Current.Pages!.Single().Keys!.Single(k => k.Position == position);

        Assert.Equal(expected, Dispatcher.ActionOf(key, gesture, viaGestures));
    }

    [Fact]
    public async Task A_key_without_on_hold_fires_when_pressed_and_only_once()
    {
        await using var rig = new KeysRig();

        rig.Dispatcher.Receive(Key(5, Gesture.Press));
        Assert.Equal(("short", Trigger.Key), await rig.NextRun());

        KeepDown(rig, 5, 300);                                  // sigue apretada: los refrescos no repiten
        rig.Dispatcher.Receive(Key(5, Gesture.Release));        // y soltarla tampoco dispara
        rig.Dispatcher.Receive(Key(12, Gesture.Press));         // centinela: un toque en la otra tecla
        rig.Dispatcher.Receive(Key(12, Gesture.Release));
        Assert.Equal(("inicio", Trigger.Key), await rig.NextRun());
        Assert.Equal(2, rig.Runs);
    }

    [Fact]
    public async Task A_key_with_on_hold_fires_its_action_when_released_before_the_second()
    {
        await using var rig = new KeysRig();

        rig.Dispatcher.Receive(Key(12, Gesture.Press));
        KeepDown(rig, 12, 300);
        rig.Dispatcher.Receive(Key(12, Gesture.Release));

        Assert.Equal(("inicio", Trigger.Key), await rig.NextRun());
        Assert.Equal(1, rig.Runs);
    }

    [Fact]
    public async Task A_key_with_on_hold_fires_the_other_action_at_the_second_without_waiting_for_the_release()
    {
        await using var rig = new KeysRig();

        rig.Dispatcher.Receive(Key(12, Gesture.Press));
        KeepDown(rig, 12, 950);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(50));
        rig.Dispatcher.Tick();                                  // el segundo se cumple sin que llegue nada

        Assert.Equal(("fin", Trigger.Key), await rig.NextRun());

        rig.Dispatcher.Receive(Key(12, Gesture.Release));       // soltar después no dispara nada
        rig.Dispatcher.Receive(Key(5, Gesture.Press));          // centinela
        Assert.Equal(("short", Trigger.Key), await rig.NextRun());
        Assert.Equal(2, rig.Runs);
    }

    [Fact]
    public async Task A_hotkey_on_a_key_with_on_hold_still_fires_the_tap_action()
    {
        await using var rig = new KeysRig();

        rig.Dispatcher.Receive(new InputEvent("hotkeys", "key.12", Gesture.Tap));

        Assert.Equal(("inicio", Trigger.Hotkey), await rig.NextRun());
    }

    [Fact]
    public async Task The_gesture_clock_runs_by_itself()
    {
        await using var rig = new KeysRig();
        using var life = new CancellationTokenSource();
        var ticking = rig.Dispatcher.KeepTickingAsync(rig.Clock, life.Token);

        rig.Dispatcher.Receive(Key(12, Gesture.Press));
        KeepDown(rig, 12, 950);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(40));
        rig.Dispatcher.Receive(Key(12, Gesture.Press));         // el último estado llega a los 990 ms
        rig.Clock.Advance(TimeSpan.FromMilliseconds(10));       // el segundo se cumple y no llega nada más

        Assert.Equal(("fin", Trigger.Key), await rig.NextRun());

        await life.CancelAsync();
        await ticking;
    }

    /// <summary>Lo que hace la placa con una tecla apretada: repite el estado cada 50 ms.</summary>
    private static void KeepDown(KeysRig rig, int key, int milliseconds)
    {
        for (var elapsed = 50; elapsed <= milliseconds; elapsed += 50)
        {
            rig.Clock.Advance(TimeSpan.FromMilliseconds(50));
            rig.Dispatcher.Receive(Key(key, Gesture.Press));
        }
    }

    private static InputEvent Key(int key, Gesture gesture) => new(SerialConsole.SourceId, $"key.{key}", gesture);
}
