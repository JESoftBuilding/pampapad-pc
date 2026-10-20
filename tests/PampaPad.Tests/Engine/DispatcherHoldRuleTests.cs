using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

public class DispatcherHoldRuleTests
{
    private const string Config = """
        {
          "version": 1,
          "pages": [ { "id": "p", "name": "P", "keys": [
            { "position": 1, "label": "Parar mal", "action": "parar", "hotkey": "Ctrl+Alt+P" },
            { "position": 2, "label": "Pausa", "action": "seguir", "onHold": { "action": "pausar" } }
          ] } ],
          "actions": [
            { "id": "parar", "name": "Parar", "steps": [ { "type": "obs.record", "do": "stop" } ] },
            { "id": "pausar", "name": "Pausar", "steps": [ { "type": "obs.record", "do": "pause" } ] },
            { "id": "seguir", "name": "Seguir", "steps": [ { "type": "obs.record", "do": "resume" } ] }
          ],
          "hotkeys": [ { "hotkey": "Ctrl+Alt+D", "action": "parar" } ]
        }
        """;

    [Fact]
    public async Task A_key_that_stops_with_a_tap_is_broken_from_the_console_and_from_its_hotkey()
    {
        await using var rig = new DispatcherRig(Config);

        rig.Dispatcher.Receive(new InputEvent("console", "key.1", Gesture.Press));
        Assert.True(rig.Windows.Press("Ctrl+Alt+P"));
        Assert.True(rig.Windows.Press("Ctrl+Alt+D"));              // el atajo general sí para: por teclado no hay mantener (02 §6)
        await Poll.Until(() => rig.Finished.Count == 1);

        ActionResult only = Assert.Single(rig.Finished);
        Assert.Equal(("parar", Trigger.Hotkey), (only.ActionId, only.RequestedBy));
        Assert.Single(rig.Record.Runs);
        Assert.Contains(rig.State.Current.Problems, problem => problem.Where == "pages[0].keys[0].action" && problem.Code == ProblemCodes.HoldRequired);
        Assert.Equal(2, rig.Log.Recent().Count(entry => entry.Code == ProblemCodes.HoldRequired));
    }

    [Fact]
    public async Task On_the_pause_key_a_tap_resumes_and_a_hold_pauses()
    {
        var clock = new FakeTimeProvider();
        await using var rig = new DispatcherRig(Config, clock: clock);

        rig.Dispatcher.Receive(new InputEvent("console", "key.2", Gesture.Press));
        rig.Dispatcher.Receive(new InputEvent("console", "key.2", Gesture.Release));      // un toque: seguir
        await Poll.Until(() => rig.Finished.Count == 1);

        rig.Dispatcher.Receive(new InputEvent("console", "key.2", Gesture.Press));
        for (int i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            rig.Dispatcher.Receive(new InputEvent("console", "key.2", Gesture.Press));
        }

        await Poll.Until(() => rig.Finished.Count == 2);

        Assert.Equal(new[] { "seguir", "pausar" }, rig.Finished.Select(result => result.ActionId));
        Assert.Equal(2, rig.Record.Runs.Count);
        Assert.DoesNotContain(rig.Log.Recent(), entry => entry.Code == ProblemCodes.HoldRequired);
    }
}
