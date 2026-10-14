using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Los bordes del paso de un estado de teclas a eventos crudos, del umbral y de los números de la spec.</summary>
public class GesturesStateTests
{
    [Fact]
    public void The_defaults_are_the_numbers_of_the_spec()
    {
        Assert.Equal(1000, Gestures.DefaultHoldMs);                                   // 02 §4.10
        Assert.Equal(TimeSpan.FromMilliseconds(150), Gestures.DefaultFreshness);      // 04 §8
    }

    [Fact]
    public void Every_state_of_the_twelve_keys_comes_out_in_key_order()
    {
        var all = Enumerable.Range(1, 12).Select(Press).ToArray();
        var none = Enumerable.Range(1, 12).Select(Release).ToArray();

        AssertEvents(Gestures.FromKeys("console", 0, 0xFFF), all);
        AssertEvents(Gestures.FromKeys("console", 0xFFF, 0xFFF), all);
        AssertEvents(Gestures.FromKeys("console", 0xFFF, 0), none);
        Assert.Empty(Gestures.FromKeys("console", 0, 0));
    }

    [Fact]
    public void A_state_changes_one_key_at_a_time_without_touching_the_others()
    {
        AssertEvents(Gestures.FromKeys("console", 0b0001_0001, 0b0001_0010), Release(1), Press(2), Press(5));
        AssertEvents(Gestures.FromKeys("console", 0b0000_0001, 0b0000_0011), Press(1), Press(2));
    }

    [Fact]
    public void The_source_of_the_state_is_the_source_of_every_event()
    {
        var events = Gestures.FromKeys("radio", 0b01, 0b10);

        Assert.Equal(["radio", "radio"], events.Select(e => e.Source));
        Assert.Equal(["key.1", "key.2"], events.Select(e => e.Control));
        Assert.Equal([Gesture.Release, Gesture.Press], events.Select(e => e.Gesture));
    }

    [Fact]
    public void Bits_above_the_twelve_keys_in_the_previous_state_are_not_releases()
    {
        Assert.Empty(Gestures.FromKeys("console", 0b1111_0000_0000_0000, 0));
        Assert.Empty(Gestures.FromKeys("console", 0b1111_0000_0000_0000, 0b1111_0000_0000_0000));
    }

    [Fact]
    public void The_sign_bit_of_a_state_is_not_a_key_zero()
    {
        Assert.Empty(Gestures.FromKeys("console", 0, int.MinValue));
        Assert.Empty(Gestures.FromKeys("console", int.MinValue, 0));
        AssertEvents(Gestures.FromKeys("console", 0, -1), Enumerable.Range(1, 12).Select(Press).ToArray());
    }

    [Fact]
    public void The_keys_of_an_input_state_go_straight_in()
    {
        var state = new InputState(0b1010_0000_0000_0101);

        AssertEvents(Gestures.FromKeys("console", 0, state.Keys), Press(1), Press(3));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(int.MinValue, 1000)]
    [InlineData(-1, 1000)]
    public void The_threshold_takes_any_positive_number_and_falls_back_on_anything_else(int holdMs, int expected)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expected), Gestures.HoldFrom(holdMs));
    }

    private static InputEvent Press(int key) => new("console", $"key.{key}", Gesture.Press);

    private static InputEvent Release(int key) => new("console", $"key.{key}", Gesture.Release);

    private static void AssertEvents(IReadOnlyList<InputEvent> actual, params InputEvent[] expected) =>
        Assert.Equal<InputEvent>(expected, actual);
}
