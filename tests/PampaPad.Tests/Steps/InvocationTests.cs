using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

// Los nombres con que Trigger y Gesture viajan al registro y al puente: 01 §3.1 y 05 §9 los escriben con minúscula inicial.
public class InvocationTests
{
    [Theory]
    [InlineData(Trigger.Key, "key")]
    [InlineData(Trigger.Hotkey, "hotkey")]
    [InlineData(Trigger.Window, "window")]
    [InlineData(Trigger.Mcp, "mcp")]
    [InlineData(Trigger.Tray, "tray")]
    [InlineData(Trigger.Internal, "internal")]
    public void Who_fired_it_travels_with_the_word_of_the_spec(Trigger trigger, string word)
    {
        Assert.Equal($"\"{word}\"", JsonSerializer.Serialize(trigger, Json.Compact));
    }

    [Theory]
    [InlineData(Gesture.Press, "press")]
    [InlineData(Gesture.Release, "release")]
    [InlineData(Gesture.Turn, "turn")]
    [InlineData(Gesture.Tap, "tap")]
    [InlineData(Gesture.Hold, "hold")]
    public void The_gesture_travels_with_the_word_of_the_spec(Gesture gesture, string word)
    {
        Assert.Equal($"\"{word}\"", JsonSerializer.Serialize(gesture, Json.Compact));
    }

    [Fact]
    public void The_enums_have_exactly_the_values_of_the_spec_and_nobody_added_one()
    {
        Assert.Equal(6, Enum.GetValues<Trigger>().Length);
        Assert.Equal(5, Enum.GetValues<Gesture>().Length);
    }

    [Fact]
    public void An_invocation_is_compared_by_value()
    {
        Guid run = Guid.NewGuid();

        Assert.Equal(
            new Invocation(run, Trigger.Hotkey, "hotkeys", "key.6", Gesture.Tap, 0),
            new Invocation(run, Trigger.Hotkey, "hotkeys", "key.6", Gesture.Tap, 0));
    }

    [Fact]
    public void An_invocation_from_the_window_or_the_tray_needs_no_control_and_no_gesture()
    {
        var fromTray = new Invocation(Guid.NewGuid(), Trigger.Tray, null, null, null, null);

        Assert.Null(fromTray.Control);
        Assert.Null(fromTray.Gesture);
        Assert.Null(fromTray.Value);
    }
}
