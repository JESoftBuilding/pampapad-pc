using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

public class GesturesTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly Gestures _gestures;

    public GesturesTests()
    {
        _gestures = new Gestures(_clock, TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public void A_press_comes_out_at_once()
    {
        AssertEvents(_gestures.Feed(Press(3)), Press(3));
    }

    [Fact]
    public void A_repeated_press_is_only_the_refresh()
    {
        _gestures.Feed(Press(3));
        Advance(50);

        Assert.Empty(_gestures.Feed(Press(3)));
    }

    [Fact]
    public void Released_before_the_threshold_is_a_release_and_a_tap()
    {
        _gestures.Feed(Press(3));
        KeepDown(3, 100);

        AssertEvents(_gestures.Feed(Release(3)), Release(3), Tap(3));
    }

    [Fact]
    public void Held_until_the_threshold_is_a_hold_without_waiting_for_the_release()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 950);
        Advance(50);

        AssertEvents(_gestures.Tick(), Hold(12));
    }

    [Fact]
    public void The_hold_also_comes_out_with_the_refresh_that_crosses_the_threshold()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 950);
        Advance(50);

        AssertEvents(_gestures.Feed(Press(12)), Hold(12));
    }

    [Fact]
    public void After_a_hold_the_release_is_not_a_tap_and_the_hold_does_not_repeat()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 1500);

        Assert.Empty(_gestures.Tick());
        AssertEvents(_gestures.Feed(Release(12)), Release(12));
    }

    [Fact]
    public void Without_a_fresh_state_the_key_counts_as_released_and_nothing_fires()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 300);
        Advance(150);

        AssertEvents(_gestures.Tick(), Release(12));
        Advance(2000);
        Assert.Empty(_gestures.Tick());
        Assert.Empty(_gestures.Feed(Release(12)));
    }

    [Fact]
    public void One_millisecond_before_the_window_closes_the_key_is_still_down()
    {
        _gestures.Feed(Press(3));
        Advance(149);

        Assert.Empty(_gestures.Tick());
        AssertEvents(_gestures.Feed(Release(3)), Release(3), Tap(3));
    }

    [Fact]
    public void A_key_that_went_stale_before_the_threshold_never_holds()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 800);
        Advance(150);

        AssertEvents(_gestures.Tick(), Release(12));
    }

    [Fact]
    public void A_stale_key_is_dropped_by_the_next_event_even_without_a_tick()
    {
        _gestures.Feed(Press(12));
        Advance(400);

        AssertEvents(_gestures.Feed(Press(5)), Release(12), Press(5));
    }

    [Fact]
    public void A_press_after_going_stale_is_a_new_press()
    {
        _gestures.Feed(Press(3));
        Advance(200);

        AssertEvents(_gestures.Feed(Press(3)), Release(3), Press(3));
    }

    [Fact]
    public void Two_keys_are_followed_separately()
    {
        _gestures.Feed(Press(12));
        Advance(50);
        _gestures.Feed(Press(12));
        AssertEvents(_gestures.Feed(Press(5)), Press(5));
        Advance(50);
        _gestures.Feed(Press(12));

        AssertEvents(_gestures.Feed(Release(5)), Release(5), Tap(5));
        AssertEvents(_gestures.Feed(Release(12)), Release(12), Tap(12));
    }

    [Fact]
    public void A_release_of_a_key_that_was_not_down_is_ignored()
    {
        Assert.Empty(_gestures.Feed(Release(7)));
    }

    [Fact]
    public void What_is_neither_press_nor_release_passes_through()
    {
        var turn = new InputEvent("console", "knob", Gesture.Turn, 1);

        AssertEvents(_gestures.Feed(turn), turn);
    }

    [Fact]
    public void A_state_becomes_a_press_for_every_key_that_is_down()
    {
        AssertEvents(Gestures.FromKeys("console", 0b0000, 0b0100), Press(3));
        AssertEvents(Gestures.FromKeys("console", 0b0100, 0b0100), Press(3));
    }

    [Fact]
    public void A_key_that_is_no_longer_down_becomes_a_release_before_the_presses()
    {
        AssertEvents(Gestures.FromKeys("console", 0b0101, 0b0110), Release(1), Press(2), Press(3));
        AssertEvents(Gestures.FromKeys("console", 0b0100, 0b0000), Release(3));
    }

    [Fact]
    public void Bits_above_the_twelve_keys_are_not_keys()
    {
        Assert.Empty(Gestures.FromKeys("console", 0, 0b0011_0000_0000_0000));
        AssertEvents(Gestures.FromKeys("console", 0, 0b1000_0000_0000), Press(12));
    }

    [Theory]
    [InlineData(null, 1000)]
    [InlineData(0, 1000)]
    [InlineData(-5, 1000)]
    [InlineData(500, 500)]
    [InlineData(3000, 3000)]
    public void The_threshold_comes_from_the_library_or_is_one_second(int? holdMs, int expected)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expected), Gestures.HoldFrom(holdMs));
    }

    private void Advance(int milliseconds) => _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));

    private void KeepDown(int key, int milliseconds)
    {
        for (var elapsed = 50; elapsed <= milliseconds; elapsed += 50)
        {
            Advance(50);
            _gestures.Feed(Press(key));
        }
    }

    private static InputEvent Press(int key) => new("console", $"key.{key}", Gesture.Press);

    private static InputEvent Release(int key) => new("console", $"key.{key}", Gesture.Release);

    private static InputEvent Tap(int key) => new("console", $"key.{key}", Gesture.Tap);

    private static InputEvent Hold(int key) => new("console", $"key.{key}", Gesture.Hold);

    private static void AssertEvents(IReadOnlyList<InputEvent> actual, params InputEvent[] expected) =>
        Assert.Equal<InputEvent>(expected, actual);
}
