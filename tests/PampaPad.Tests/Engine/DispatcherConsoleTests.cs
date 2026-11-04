using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La perilla y la tecla del módulo, con Gestures y reloj falso (plan de la 9a, decisiones 8 y 9; E9-6).</summary>
public class DispatcherConsoleTests
{
    private const string Config = """
        {
          "version": 1,
          "mainPage": "toma",
          "moduleKey": { "layer": "capa" },
          "pages": [
            { "id": "toma", "keys": [ { "position": 3, "action": "en-toma" } ] },
            { "id": "sala", "keys": [ { "position": 3, "action": "en-sala" } ] },
            { "id": "capa", "show": false, "keys": [
              { "position": 3, "action": "en-capa" },
              { "position": 4, "action": "toque-capa", "onHold": { "action": "mantenido-capa" } }
            ] },
            { "id": "extra", "keys": [] }
          ],
          "actions": [
            { "id": "en-toma", "steps": [ { "type": "obs.marker", "marker": "toma" } ] },
            { "id": "en-sala", "steps": [ { "type": "obs.marker", "marker": "sala" } ] },
            { "id": "en-capa", "steps": [ { "type": "obs.marker", "marker": "capa" } ] },
            { "id": "toque-capa", "steps": [ { "type": "obs.marker", "marker": "toque" } ] },
            { "id": "mantenido-capa", "steps": [ { "type": "obs.marker", "marker": "mantenido" } ] }
          ]
        }
        """;

    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public async Task Two_detents_to_the_right_move_two_pages_and_wrap_around()
    {
        await using var rig = Rig();

        Turn(rig, +2);
        Assert.Equal("extra", rig.State.Current.BasePage);

        Turn(rig, +1);
        Assert.Equal("toma", rig.State.Current.BasePage);
    }

    [Fact]
    public async Task One_detent_to_the_left_goes_back()
    {
        await using var rig = Rig();

        Turn(rig, -1);

        Assert.Equal("extra", rig.State.Current.BasePage);
    }

    [Theory]
    [InlineData(RecordingState.Recording)]
    [InlineData(RecordingState.Paused)]
    [InlineData(RecordingState.Starting)]
    [InlineData(RecordingState.Stopping)]
    public async Task During_a_take_the_knob_does_not_move_and_logs_nothing(RecordingState recording)
    {
        await using var rig = Rig();
        rig.State.Update(s => s with { Obs = s.Obs with { Recording = recording } });
        int before = rig.Log.Recent().Count;

        Turn(rig, +1);
        Turn(rig, -3);

        Assert.Equal("toma", rig.State.Current.BasePage);
        Assert.Equal(before, rig.Log.Recent().Count);
    }

    [Fact]
    public async Task The_knob_click_does_nothing()
    {
        await using var rig = Rig();
        rig.Dispatcher.Go("sala");

        Send(rig, Controls.Knob, Gesture.Press);
        Send(rig, Controls.Knob, Gesture.Release);

        Assert.Equal("sala", rig.State.Current.BasePage);
        Assert.Empty(rig.Marker.Runs);
    }

    [Fact]
    public async Task A_tap_of_the_module_key_goes_to_the_main_page()
    {
        await using var rig = Rig();
        rig.Dispatcher.Go("sala");

        Send(rig, Controls.ModuleKey, Gesture.Press);
        Send(rig, Controls.ModuleKey, Gesture.Release);

        Assert.Equal(("toma", "toma"), (rig.State.Current.BasePage, rig.State.Current.ActivePage));
    }

    [Fact]
    public async Task Held_it_shows_the_layer_and_on_release_the_base_comes_back()
    {
        await using var rig = Rig();
        rig.Dispatcher.Go("sala");

        Send(rig, Controls.ModuleKey, Gesture.Press);
        Assert.Equal(("sala", "capa"), (rig.State.Current.BasePage, rig.State.Current.ActivePage));

        HoldPast(rig, Controls.ModuleKey);
        Send(rig, Controls.ModuleKey, Gesture.Release);
        Assert.Equal(("sala", "sala"), (rig.State.Current.BasePage, rig.State.Current.ActivePage));
    }

    [Fact]
    public async Task Module_plus_key_3_runs_key_3_of_the_layer_and_releasing_in_any_order_leaves_the_base()
    {
        await using var rig = Rig();
        rig.Dispatcher.Go("sala");

        Send(rig, Controls.ModuleKey, Gesture.Press);
        Send(rig, "key.3", Gesture.Press);
        Send(rig, Controls.ModuleKey, Gesture.Release);           // primero la del módulo
        Send(rig, "key.3", Gesture.Release);
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Equal("en-capa", Assert.Single(rig.Finished).ActionId);
        Assert.Equal(("sala", "sala"), (rig.State.Current.BasePage, rig.State.Current.ActivePage));
    }

    [Fact]
    public async Task A_layer_key_with_on_hold_released_after_the_module_key_still_fires_the_layer_tap()
    {
        await using var rig = Rig();

        Send(rig, Controls.ModuleKey, Gesture.Press);
        Send(rig, "key.4", Gesture.Press);
        Send(rig, Controls.ModuleKey, Gesture.Release);
        Send(rig, "key.4", Gesture.Release);
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Equal("toque-capa", Assert.Single(rig.Finished).ActionId);
        Assert.Equal("toma", rig.State.Current.ActivePage);
    }

    [Fact]
    public async Task During_a_take_module_plus_key_runs_the_layer_action()
    {
        await using var rig = Rig();
        rig.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });

        Send(rig, Controls.ModuleKey, Gesture.Press);
        Assert.Equal("capa", rig.State.Current.ActivePage);
        Send(rig, "key.3", Gesture.Press);
        await Poll.Until(() => rig.Finished.Count == 1);
        Send(rig, "key.3", Gesture.Release);
        Send(rig, Controls.ModuleKey, Gesture.Release);

        Assert.Equal("en-capa", Assert.Single(rig.Finished).ActionId);
        Assert.Equal("toma", rig.State.Current.ActivePage);
    }

    [Fact]
    public async Task Without_a_layer_holding_the_module_key_does_nothing()
    {
        await using var rig = Rig(Config.Replace("\"moduleKey\": { \"layer\": \"capa\" },", "", StringComparison.Ordinal));
        rig.Dispatcher.Go("sala");

        Send(rig, Controls.ModuleKey, Gesture.Press);
        Assert.Equal("sala", rig.State.Current.ActivePage);
        Send(rig, "key.3", Gesture.Press);
        await Poll.Until(() => rig.Finished.Count == 1);
        HoldPast(rig, Controls.ModuleKey);
        Send(rig, Controls.ModuleKey, Gesture.Release);

        Assert.Equal("en-sala", Assert.Single(rig.Finished).ActionId);
        Assert.Equal(("sala", "sala"), (rig.State.Current.BasePage, rig.State.Current.ActivePage));
    }

    [Fact]
    public async Task A_stale_module_key_is_given_up_and_the_layer_does_not_stick()
    {
        await using var rig = Rig();

        Send(rig, Controls.ModuleKey, Gesture.Press);
        _clock.Advance(Gestures.DefaultFreshness);
        rig.Dispatcher.Tick();

        Assert.Equal("toma", rig.State.Current.ActivePage);
    }

    private DispatcherRig Rig(string config = Config) => new(config, clock: _clock);

    private static void Send(DispatcherRig rig, string control, Gesture gesture, int value = 0) =>
        rig.Dispatcher.Receive(new InputEvent("console", control, gesture, value));

    private static void Turn(DispatcherRig rig, int detents) => Send(rig, Controls.Knob, Gesture.Turn, detents);

    // Mantiene la tecla más allá de holdMs, refrescándola como lo hace la consola cada 50 ms.
    private void HoldPast(DispatcherRig rig, string control)
    {
        for (int i = 0; i < 25; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(50));
            Send(rig, control, Gesture.Press);
        }
    }

    // Revisión de la tanda 2 de la 9a (should): recargar la biblioteca con la tecla del módulo mantenida deja la capa mostrada.
    [Fact]
    public async Task A_refresh_with_the_module_key_held_keeps_the_layer_shown()
    {
        await using var rig = Rig();
        rig.Dispatcher.Go("sala");
        Send(rig, Controls.ModuleKey, Gesture.Press);
        Assert.Equal("capa", rig.State.Current.ActivePage);

        rig.Dispatcher.Refresh();

        Assert.Equal(("sala", "capa"), (rig.State.Current.BasePage, rig.State.Current.ActivePage));
    }
}
