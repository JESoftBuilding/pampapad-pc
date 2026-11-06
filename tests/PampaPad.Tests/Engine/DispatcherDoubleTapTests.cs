using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El doble toque de la tecla del módulo corre moduleKey.doubleTap (plan de la 1.3.0, decisión 4), con Gestures y reloj falso.</summary>
public class DispatcherDoubleTapTests
{
    private const string Config = """
        {
          "version": 1,
          "mainPage": "toma",
          "moduleKey": { "layer": "capa", "doubleTap": "doble" },
          "pages": [
            { "id": "toma", "keys": [] },
            { "id": "sala", "keys": [] },
            { "id": "capa", "show": false, "keys": [ { "position": 3, "action": "en-capa" } ] }
          ],
          "actions": [
            { "id": "doble", "steps": [ { "type": "obs.marker", "marker": "doble" } ] },
            { "id": "en-capa", "steps": [ { "type": "obs.marker", "marker": "capa" } ] }
          ]
        }
        """;

    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public async Task A_double_tap_runs_its_action_and_does_not_go_to_the_main_page()
    {
        await using var rig = Rig();
        rig.Dispatcher.Go("sala");

        Tap(rig);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Tap(rig);
        await Poll.Until(() => rig.Finished.Count == 1);
        Settle(rig);

        Assert.Equal("doble", Assert.Single(rig.Finished).ActionId);
        Assert.Equal("sala", rig.State.Current.BasePage);
    }

    [Fact]
    public async Task A_single_tap_goes_to_the_main_page_300_ms_later()
    {
        await using var rig = Rig();
        rig.Dispatcher.Go("sala");

        Tap(rig);
        Assert.Equal("sala", rig.State.Current.BasePage);
        Settle(rig);

        Assert.Equal("toma", rig.State.Current.BasePage);
        Assert.Empty(rig.Finished);
    }

    [Fact]
    public async Task A_combination_first_does_not_count_as_the_first_tap()
    {
        await using var rig = Rig();

        Send(rig, Controls.ModuleKey, Gesture.Press);
        Send(rig, "key.3", Gesture.Press);
        Send(rig, "key.3", Gesture.Release);
        Send(rig, Controls.ModuleKey, Gesture.Release);
        await Poll.Until(() => rig.Finished.Count == 1);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Tap(rig);
        Settle(rig);

        Assert.Equal("en-capa", Assert.Single(rig.Finished).ActionId);
    }

    [Fact]
    public async Task A_combination_second_does_not_fire_the_double_tap()
    {
        await using var rig = Rig();
        rig.Dispatcher.Go("sala");

        Tap(rig);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Send(rig, Controls.ModuleKey, Gesture.Press);
        Send(rig, "key.3", Gesture.Press);
        Send(rig, "key.3", Gesture.Release);
        Send(rig, Controls.ModuleKey, Gesture.Release);
        await Poll.Until(() => rig.Finished.Count == 1);
        Settle(rig);

        Assert.Equal("en-capa", Assert.Single(rig.Finished).ActionId);
        Assert.Equal("sala", rig.State.Current.BasePage);
    }

    [Fact]
    public async Task During_a_take_the_double_tap_action_runs()
    {
        await using var rig = Rig();
        rig.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });

        Tap(rig);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Tap(rig);
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Equal("doble", Assert.Single(rig.Finished).ActionId);
    }

    private DispatcherRig Rig() => new(Config, clock: _clock);

    private static void Send(DispatcherRig rig, string control, Gesture gesture) =>
        rig.Dispatcher.Receive(new InputEvent("console", control, gesture));

    private static void Tap(DispatcherRig rig)
    {
        Send(rig, Controls.ModuleKey, Gesture.Press);
        Send(rig, Controls.ModuleKey, Gesture.Release);
    }

    // Deja pasar la ventana del doble toque: un toque guardado sale con el Tick.
    private void Settle(DispatcherRig rig)
    {
        _clock.Advance(Gestures.DoubleTapWindow);
        rig.Dispatcher.Tick();
    }
}
