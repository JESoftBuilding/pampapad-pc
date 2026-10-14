using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que las pruebas del plan no fijan del despachador: qué evento corre qué, qué queda anotado y qué pasa cuando algo sale mal.</summary>
public class DispatcherEdgeTests
{
    private const string Short = DispatcherRig.Short;

    private const string WithOnHold = """
        {
          "version": 1,
          "pages": [ { "id": "p", "name": "P", "keys": [
            { "position": 5, "label": "Short", "action": "marca-short", "hotkey": "Ctrl+Shift+Alt+F9", "onHold": { "action": "marca-largo" } }
          ] } ],
          "actions": [
            { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] },
            { "id": "marca-largo", "name": "Marca: largo", "steps": [ { "type": "obs.marker", "marker": "largo" } ] }
          ]
        }
        """;

    private const string WithGeneral = """
        {
          "version": 1,
          "pages": [ { "id": "p", "name": "P", "keys": [ { "position": 5, "label": "Short", "action": "marca-short" } ] } ],
          "actions": [
            { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] },
            { "id": "marca-largo", "name": "Marca: largo", "steps": [ { "type": "obs.marker", "marker": "largo" } ] }
          ],
          "hotkeys": [ { "hotkey": "Ctrl+Alt+X", "action": "marca-short" }, { "hotkey": "Ctrl+Alt+Y", "action": "marca-largo" } ]
        }
        """;

    private const string WithNulls = """
        {
          "version": 1,
          "pages": [ { "id": "p", "name": "P", "keys": [ null ] } ],
          "actions": [ null ],
          "hotkeys": [ null ]
        }
        """;

    [Theory]
    [InlineData(Gesture.Press)]
    [InlineData(Gesture.Release)]
    [InlineData(Gesture.Turn)]
    [InlineData(Gesture.Hold)]
    public async Task Only_a_tap_runs_an_action(Gesture gesture)
    {
        await using var rig = new DispatcherRig(WithGeneral);

        rig.Dispatcher.Receive(new InputEvent("console", "key.5", gesture));
        rig.Dispatcher.Receive(new InputEvent("hotkeys", "hotkey", gesture, 0));
        rig.Dispatcher.Receive(new InputEvent("hotkeys", "hotkey", gesture, 1));

        Assert.Empty(rig.Marker.Runs);
        Assert.Empty(rig.State.Current.Running);
        Assert.Empty(rig.Finished);
    }

    [Theory]
    [InlineData("key.0")]
    [InlineData("key.1")]
    [InlineData("key.13")]
    [InlineData("key.-1")]
    [InlineData("key.5x")]
    [InlineData("key.")]
    [InlineData("knob")]
    [InlineData("moduleKey")]
    [InlineData("")]
    public async Task A_control_that_names_no_key_with_an_action_does_nothing(string control)
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);

        rig.Dispatcher.Receive(new InputEvent("console", control, Gesture.Tap));

        Assert.Empty(rig.Marker.Runs);
        Assert.Empty(rig.Finished);
        Assert.Empty(rig.Errors);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(99)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public async Task A_general_hotkey_that_is_not_in_the_list_does_nothing(int place)
    {
        await using var rig = new DispatcherRig(WithGeneral);

        rig.Dispatcher.Receive(new InputEvent("hotkeys", "hotkey", Gesture.Tap, place));

        Assert.Empty(rig.Marker.Runs);
        Assert.Empty(rig.Finished);
        Assert.Empty(rig.Errors);
    }

    [Theory]
    [InlineData(0, "marca-short")]
    [InlineData(1, "marca-largo")]
    public async Task A_general_hotkey_runs_the_action_at_its_place_in_the_list(int place, string expected)
    {
        await using var rig = new DispatcherRig(WithGeneral);

        rig.Dispatcher.Receive(new InputEvent("hotkeys", "hotkey", Gesture.Tap, place));
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Equal(expected, Assert.Single(rig.Finished).ActionId);
    }

    [Fact]
    public async Task Empty_places_in_the_file_do_not_break_an_event()
    {
        await using var rig = new DispatcherRig(WithNulls);

        rig.Dispatcher.Receive(new InputEvent("hotkeys", "hotkey", Gesture.Tap));
        rig.Dispatcher.Receive(new InputEvent("console", "key.1", Gesture.Tap));

        Assert.Empty(rig.Finished);
        Assert.Empty(rig.Errors);
    }

    [Fact]
    public async Task An_event_from_the_console_is_a_key_and_carries_what_the_step_needs_to_know()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        Invocation? seen = null;
        rig.Marker.Behavior = (_, invocation, _) =>
        {
            seen = invocation;
            return Task.FromResult(new StepResult(null));
        };

        rig.Dispatcher.Receive(new InputEvent("console", "key.5", Gesture.Tap, 7));
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Equal(Trigger.Key, Assert.Single(rig.Finished).RequestedBy);
        Assert.Equal(new[] { "console", "key.5", "Tap", "7" }, new[] { seen!.Source, seen.Control, seen.Gesture.ToString(), seen.Value.ToString() });
        Assert.Equal(Trigger.Key, seen.RequestedBy);
    }

    [Fact]
    public async Task A_hotkey_of_a_key_and_a_general_one_carry_their_control_and_place()
    {
        const string both = """
            {
              "version": 1,
              "pages": [ { "id": "p", "name": "P", "keys": [ { "position": 5, "label": "Short", "action": "marca-short", "hotkey": "Ctrl+Shift+Alt+F9" } ] } ],
              "actions": [ { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] } ],
              "hotkeys": [ { "hotkey": "Ctrl+Alt+X", "action": "marca-short" }, { "hotkey": "Ctrl+Alt+Y", "action": "marca-short" } ]
            }
            """;
        await using var rig = new DispatcherRig(both);
        var seen = new List<Invocation>();
        rig.Marker.Behavior = (_, invocation, _) =>
        {
            lock (seen)
            {
                seen.Add(invocation);
            }

            return Task.FromResult(new StepResult(null));
        };

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 1);
        rig.Windows.Press("Ctrl+Alt+Y");
        await Poll.Until(() => rig.Finished.Count == 2);

        Assert.Equal(("hotkeys", "key.5", 0), (seen[0].Source, seen[0].Control, seen[0].Value));
        Assert.Equal(("hotkeys", "hotkey", 1), (seen[1].Source, seen[1].Control, seen[1].Value));
        Assert.All(seen, invocation => Assert.Equal((Trigger.Hotkey, Gesture.Tap), (invocation.RequestedBy, invocation.Gesture)));
    }

    [Fact]
    public async Task A_tap_never_runs_the_onHold_action()
    {
        await using var rig = new DispatcherRig(WithOnHold);

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Equal("marca-short", Assert.Single(rig.Finished).ActionId);
        Assert.Equal("short", Assert.Single(rig.Marker.Runs).Parameters["marker"].GetString());
    }
}
