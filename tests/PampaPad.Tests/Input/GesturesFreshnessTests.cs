using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// Lo que sale cuando el umbral de mantener y la pérdida de frescura caen cerca: el resultado tiene
/// que ser el mismo se llame a Tick cada 10 ms (como hará el Dispatcher) o nunca.
/// </summary>
public class GesturesFreshnessTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly Gestures _gestures;

    public GesturesFreshnessTests()
    {
        _gestures = new Gestures(_clock, TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public void With_a_tick_every_10_ms_a_key_that_crosses_the_threshold_while_fresh_holds_and_then_goes_stale()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 900);

        var seen = new List<(int Ms, InputEvent Event)>();
        for (var ms = 910; ms <= 1200; ms += 10)
        {
            Advance(10);
            seen.AddRange(_gestures.Tick().Select(e => (ms, e)));
        }

        Assert.Equal<(int, InputEvent)>([(1000, Hold(12)), (1050, Release(12))], seen);
    }

    [Fact]
    public void Without_a_tick_the_same_key_gives_the_same_events_in_one_pass()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 900);
        Advance(150);

        AssertEvents(_gestures.Tick(), Hold(12), Release(12));
    }

    [Fact]
    public void The_next_event_without_a_tick_also_reports_the_hold_before_the_release()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 900);
        Advance(150);

        AssertEvents(_gestures.Feed(Press(5)), Hold(12), Release(12), Press(5));
    }

    [Fact]
    public void A_release_that_arrives_after_the_key_went_stale_is_ignored_but_the_hold_is_not_lost()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 900);
        Advance(150);

        AssertEvents(_gestures.Feed(Release(12)), Hold(12), Release(12));
    }

    [Fact]
    public void A_late_tick_does_not_turn_a_key_that_went_stale_before_the_threshold_into_a_hold()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 800);
        Advance(300);

        AssertEvents(_gestures.Tick(), Release(12));
    }

    [Theory]
    [InlineData(800, false)]
    [InlineData(850, false)]
    [InlineData(851, true)]
    [InlineData(900, true)]
    public void The_hold_counts_only_if_the_threshold_falls_strictly_before_the_window_closes(int lastRefreshMs, bool holds)
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 800);
        if (lastRefreshMs > 800)
        {
            Advance(lastRefreshMs - 800);
            _gestures.Feed(Press(12));
        }

        Advance(150);

        InputEvent[] expected = holds ? [Hold(12), Release(12)] : [Release(12)];
        AssertEvents(_gestures.Tick(), expected);
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

    private static InputEvent Hold(int key) => new("console", $"key.{key}", Gesture.Hold);

    private static void AssertEvents(IReadOnlyList<InputEvent> actual, params InputEvent[] expected) =>
        Assert.Equal<InputEvent>(expected, actual);
}
