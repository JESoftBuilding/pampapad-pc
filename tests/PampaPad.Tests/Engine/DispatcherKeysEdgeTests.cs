using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que las pruebas del plan no fijan de las teclas: lo que la tabla de gestos no nombra, los bordes del reloj y los atajos de la lista general.</summary>
public class DispatcherKeysEdgeTests
{
    private const string WithGeneral = """
        {
          "version": 1,
          "mainPage": "grabacion",
          "pages": [ { "id": "grabacion", "name": "Grabación", "keys": [
            { "position": 5, "label": "Short", "action": "marca-short" },
            { "position": 12, "label": "Tramo", "action": "marca-inicio", "onHold": { "action": "marca-fin" } }
          ] } ],
          "actions": [
            { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] },
            { "id": "marca-inicio", "name": "Marca: inicio", "steps": [ { "type": "obs.marker", "marker": "inicio" } ] },
            { "id": "marca-fin", "name": "Marca: fin", "steps": [ { "type": "obs.marker", "marker": "fin" } ] }
          ],
          "hotkeys": [ { "hotkey": "Ctrl+Alt+X", "action": "marca-short" } ]
        }
        """;

    [Theory]
    [InlineData(false, Gesture.Turn, true)]
    [InlineData(false, Gesture.Press, false)]
    [InlineData(false, Gesture.Release, false)]
    [InlineData(false, Gesture.Hold, false)]
    [InlineData(true, Gesture.Turn, true)]
    [InlineData(true, Gesture.Press, false)]
    [InlineData(true, Gesture.Release, false)]
    public void The_table_gives_nothing_for_the_gestures_it_does_not_name(bool withOnHold, Gesture gesture, bool viaGestures)
    {
        Key key = new() { Position = 1, Action = "a", OnHold = withOnHold ? new HoldAction { Action = "b" } : null };

        Assert.Null(Dispatcher.ActionOf(key, gesture, viaGestures));
    }

    [Theory]
    [InlineData(Gesture.Press, true)]
    [InlineData(Gesture.Tap, true)]
    [InlineData(Gesture.Tap, false)]
    public void A_key_without_an_action_gives_nothing(Gesture gesture, bool viaGestures)
    {
        Assert.Null(Dispatcher.ActionOf(new Key { Position = 1 }, gesture, viaGestures));
        Assert.Null(Dispatcher.ActionOf(new Key { Position = 1, OnHold = new HoldAction() }, gesture, viaGestures));
    }

    [Fact]
    public void An_on_hold_without_an_action_gives_nothing_when_held()
    {
        var key = new Key { Position = 1, Action = "a", OnHold = new HoldAction() };

        Assert.Null(Dispatcher.ActionOf(key, Gesture.Hold, viaGestures: true));
        Assert.Equal("a", Dispatcher.ActionOf(key, Gesture.Tap, viaGestures: true));
    }

    [Fact]
    public void The_gesture_clock_ticks_every_ten_milliseconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(10), Dispatcher.GestureTick);
    }

    [Fact]
    public async Task A_key_the_page_does_not_have_does_nothing()
    {
        await using var rig = new KeysRig();

        rig.Dispatcher.Receive(Raw(3, Gesture.Press));
        rig.Dispatcher.Receive(Raw(3, Gesture.Release));
        rig.Dispatcher.Receive(new InputEvent(SerialConsole.SourceId, "key.3", Gesture.Tap));

        Assert.Empty(rig.Store.Current.Running);
        Assert.Equal(0, rig.Runs);
    }

    [Fact]
    public async Task A_tap_that_already_came_recognized_from_the_console_runs_the_tap_action_and_a_hold_does_not()
    {
        await using var rig = new KeysRig();

        rig.Dispatcher.Receive(new InputEvent(SerialConsole.SourceId, "key.12", Gesture.Hold));
        Assert.Empty(rig.Store.Current.Running);

        rig.Dispatcher.Receive(new InputEvent(SerialConsole.SourceId, "key.12", Gesture.Tap));

        Assert.Equal(("inicio", Trigger.Key), await rig.NextRun());
        Assert.Equal(1, rig.Runs);
    }

    [Fact]
    public async Task Two_keys_down_together_fire_each_its_own_action()
    {
        await using var rig = new KeysRig();

        rig.Dispatcher.Receive(Raw(12, Gesture.Press));
        rig.Dispatcher.Receive(Raw(5, Gesture.Press));
        Assert.Equal(("short", Trigger.Key), await rig.NextRun());

        rig.Clock.Advance(TimeSpan.FromMilliseconds(100));
        rig.Dispatcher.Receive(Raw(5, Gesture.Release));
        rig.Dispatcher.Receive(Raw(12, Gesture.Release));
        Assert.Equal(("inicio", Trigger.Key), await rig.NextRun());
        Assert.Equal(2, rig.Runs);
    }

    [Fact]
    public async Task A_key_without_on_hold_held_past_the_second_fires_once_and_the_hold_does_nothing()
    {
        await using var rig = new KeysRig();

        rig.Dispatcher.Receive(Raw(5, Gesture.Press));
        Assert.Equal(("short", Trigger.Key), await rig.NextRun());
        for (var elapsed = 50; elapsed <= 1500; elapsed += 50)
        {
            rig.Clock.Advance(TimeSpan.FromMilliseconds(50));
            rig.Dispatcher.Receive(Raw(5, Gesture.Press));
            rig.Dispatcher.Tick();
        }

        rig.Dispatcher.Receive(Raw(5, Gesture.Release));
        rig.Dispatcher.Receive(Raw(12, Gesture.Press));         // centinela: la otra tecla, un toque
        rig.Dispatcher.Receive(Raw(12, Gesture.Release));
        Assert.Equal(("inicio", Trigger.Key), await rig.NextRun());
        Assert.Equal(2, rig.Runs);
    }

    [Fact]
    public async Task Pressing_the_same_key_again_is_another_run()
    {
        await using var rig = new KeysRig();

        rig.Dispatcher.Receive(Raw(5, Gesture.Press));
        Assert.Equal(("short", Trigger.Key), await rig.NextRun());
        await Poll.Until(() => rig.Store.Current.Running.Count == 0);   // la misma acción no se pisa: se espera a que termine
        rig.Clock.Advance(TimeSpan.FromMilliseconds(50));
        rig.Dispatcher.Receive(Raw(5, Gesture.Release));
        rig.Clock.Advance(TimeSpan.FromMilliseconds(50));
        rig.Dispatcher.Receive(Raw(5, Gesture.Press));

        Assert.Equal(("short", Trigger.Key), await rig.NextRun());
        Assert.Equal(2, rig.Runs);
    }

    [Fact]
    public async Task A_raw_press_and_release_of_a_general_hotkey_never_fire_it()
    {
        await using var rig = new KeysRig(WithGeneral);

        rig.Dispatcher.Receive(new InputEvent("hotkeys", "hotkey", Gesture.Press, 0));
        rig.Dispatcher.Receive(new InputEvent("hotkeys", "hotkey", Gesture.Release, 0));
        Assert.Empty(rig.Store.Current.Running);

        rig.Dispatcher.Receive(new InputEvent("hotkeys", "hotkey", Gesture.Tap, 0));   // el toque del teclado sí
        Assert.Equal(("short", Trigger.Hotkey), await rig.NextRun());
        Assert.Equal(1, rig.Runs);
    }

    [Fact]
    public async Task Receive_and_Tick_never_throw_and_leave_the_error_in_the_log()
    {
        var clock = new BrokenClock();
        await using var rig = new KeysRig(clock: clock);

        clock.Broken = true;
        rig.Dispatcher.Receive(Raw(5, Gesture.Press));
        Assert.Contains(rig.Log.Recent(), entry => entry.Code == ErrorCodes.InternalError && entry.Message.Contains("key.5", StringComparison.Ordinal));

        clock.Broken = false;
        rig.Dispatcher.Receive(Raw(12, Gesture.Press));
        clock.Broken = true;
        rig.Dispatcher.Tick();
        Assert.Contains(rig.Log.Recent(), entry => entry.Code == ErrorCodes.InternalError && entry.Message.Contains("reloj de los gestos", StringComparison.Ordinal));
        Assert.Equal(0, rig.Runs);
    }

    [Fact]
    public async Task The_tick_loop_ends_quietly_when_cancelled_and_says_so_once()
    {
        await using var rig = new KeysRig();
        using var life = new CancellationTokenSource();
        var ticking = rig.Dispatcher.KeepTickingAsync(rig.Clock, life.Token);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));             // cien períodos con nada apretado

        await life.CancelAsync();
        await ticking;

        Assert.True(ticking.IsCompletedSuccessfully);
        Assert.Single(rig.Log.Recent(), entry => entry.Message == "Se detuvo el reloj de los gestos.");
    }

    [Fact]
    public async Task The_tick_loop_started_with_a_token_already_cancelled_ends_the_same_way()
    {
        await using var rig = new KeysRig();
        using var life = new CancellationTokenSource();
        await life.CancelAsync();

        await rig.Dispatcher.KeepTickingAsync(rig.Clock, life.Token);

        Assert.Single(rig.Log.Recent(), entry => entry.Message == "Se detuvo el reloj de los gestos.");
    }

    [Fact]
    public async Task The_invocation_says_where_it_came_from_and_with_which_gesture()
    {
        await using var rig = new KeysRig(WithGeneral);

        rig.Dispatcher.Receive(Raw(5, Gesture.Press));
        Assert.Equal(("short", Trigger.Key), await rig.NextRun());
        rig.Dispatcher.Receive(Raw(12, Gesture.Press));
        rig.Clock.Advance(TimeSpan.FromMilliseconds(100));
        rig.Dispatcher.Receive(Raw(12, Gesture.Release));
        Assert.Equal(("inicio", Trigger.Key), await rig.NextRun());
        rig.Dispatcher.Receive(new InputEvent("hotkeys", "hotkey", Gesture.Tap, 0));
        Assert.Equal(("short", Trigger.Hotkey), await rig.NextRun());

        var expected = new (Trigger, string?, string?, Gesture?, int?)[]
        {
            (Trigger.Key, "console", "key.5", Gesture.Press, 0),
            (Trigger.Key, "console", "key.12", Gesture.Tap, 0),
            (Trigger.Hotkey, "hotkeys", "hotkey", Gesture.Tap, 0),
        };
        Assert.Equal(expected, rig.Invocations.Select(i => (i.RequestedBy, i.Source, i.Control, i.Gesture, i.Value)).ToArray());
    }

    [Fact]
    public async Task A_hold_reaches_its_action_as_a_hold()
    {
        await using var rig = new KeysRig();

        rig.Dispatcher.Receive(Raw(12, Gesture.Press));
        for (var elapsed = 50; elapsed <= 950; elapsed += 50)   // un estado cada 50 ms, como la placa
        {
            rig.Clock.Advance(TimeSpan.FromMilliseconds(50));
            rig.Dispatcher.Receive(Raw(12, Gesture.Press));
        }

        rig.Clock.Advance(TimeSpan.FromMilliseconds(50));
        rig.Dispatcher.Tick();

        Assert.Equal(("fin", Trigger.Key), await rig.NextRun());
        var invocation = Assert.Single(rig.Invocations);
        Assert.Equal(("console", "key.12", Gesture.Hold), (invocation.Source, invocation.Control, invocation.Gesture));
    }

    [Fact]
    public async Task The_tick_loop_follows_the_clock_it_is_given_and_not_the_wall_clock()
    {
        var clock = new CountingClock();
        await using var rig = new KeysRig(clock: clock);
        using var life = new CancellationTokenSource();
        var ticking = rig.Dispatcher.KeepTickingAsync(rig.Clock, life.Token);
        rig.Dispatcher.Receive(Raw(12, Gesture.Press));         // con una tecla apretada cada Tick lee el reloj
        var before = clock.Reads;

        await Task.Delay(100);                                  // pasa tiempo de verdad, pero no el del reloj falso
        Assert.Equal(before, clock.Reads);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(9));        // menos que un período
        await Task.Delay(50);
        Assert.Equal(before, clock.Reads);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));        // el período se cumple
        await Poll.Until(() => clock.Reads > before);

        await life.CancelAsync();
        await ticking;
    }

    private static InputEvent Raw(int key, Gesture gesture) => new(SerialConsole.SourceId, $"key.{key}", gesture);

    /// <summary>Un reloj falso que cuenta cuántas veces se le pide la hora.</summary>
    private sealed class CountingClock : FakeTimeProvider
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public override long GetTimestamp()
        {
            Interlocked.Increment(ref _reads);
            return base.GetTimestamp();
        }
    }

    /// <summary>Un reloj falso que se puede romper: lo que Gestures lee con GetTimestamp tira.</summary>
    private sealed class BrokenClock : FakeTimeProvider
    {
        public volatile bool Broken;

        public override long GetTimestamp() => Broken ? throw new InvalidOperationException("reloj roto a propósito") : base.GetTimestamp();
    }
}
