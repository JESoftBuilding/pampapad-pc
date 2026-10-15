using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Los bordes de Gestures que las pruebas del plan no fijan: el umbral al milisegundo, el cable que se va y vuelve, el orden y los hilos.</summary>
public class GesturesEdgeTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly Gestures _gestures;

    public GesturesEdgeTests()
    {
        _gestures = new Gestures(_clock, TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public void The_hold_comes_out_at_the_threshold_and_not_a_millisecond_before()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 950);
        Advance(49);
        Assert.Empty(_gestures.Tick());

        Advance(1);
        AssertEvents(_gestures.Tick(), Hold(12));
    }

    [Fact]
    public void The_hold_is_counted_from_the_first_press_and_not_from_the_last_refresh()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 900);
        Advance(100);

        AssertEvents(_gestures.Feed(Press(12)), Hold(12));
    }

    [Fact]
    public void Nothing_else_comes_out_after_the_hold_while_the_key_stays_down()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 1000);

        for (var i = 0; i < 20; i++)
        {
            Advance(50);
            Assert.Empty(_gestures.Feed(Press(12)));
            Assert.Empty(_gestures.Tick());
        }
    }

    [Fact]
    public void A_release_after_the_threshold_with_nobody_ticking_still_reports_the_hold_first()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 950);
        Advance(60);

        AssertEvents(_gestures.Feed(Release(12)), Hold(12), Release(12));
    }

    [Fact]
    public void A_key_that_goes_stale_after_the_hold_is_a_release_without_a_tap()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 1200);
        Advance(150);

        AssertEvents(_gestures.Tick(), Release(12));
    }

    [Fact]
    public void A_key_can_hold_again_after_it_was_released()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 1100);
        AssertEvents(_gestures.Feed(Release(12)), Release(12));

        AssertEvents(_gestures.Feed(Press(12)), Press(12));
        KeepDown(12, 950);
        Advance(50);
        AssertEvents(_gestures.Tick(), Hold(12));
    }

    [Fact]
    public void A_release_and_a_press_at_the_same_instant_are_a_tap_and_a_new_press()
    {
        _gestures.Feed(Press(3));

        AssertEvents(_gestures.Feed(Release(3)), Release(3), Tap(3));
        AssertEvents(_gestures.Feed(Press(3)), Press(3));
    }

    [Fact]
    public void A_cable_that_goes_away_with_a_key_down_gives_one_release_and_never_a_hold()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 500);

        var everything = new List<InputEvent>();
        for (var i = 0; i < 100; i++)
        {
            Advance(50);
            everything.AddRange(_gestures.Tick());
        }

        AssertEvents(everything, Release(12));
    }

    [Fact]
    public void When_the_cable_comes_back_the_key_starts_clean_and_the_old_press_does_not_count()
    {
        _gestures.Feed(Press(12));
        KeepDown(12, 500);
        Advance(150);
        AssertEvents(_gestures.Tick(), Release(12));
        Advance(2000);

        AssertEvents(_gestures.Feed(Press(12)), Press(12));
        KeepDown(12, 900);
        Assert.Empty(_gestures.Tick());
        Advance(100);
        AssertEvents(_gestures.Feed(Press(12)), Hold(12));
    }

    [Fact]
    public void Of_two_keys_the_one_that_stops_arriving_goes_stale_and_the_other_does_not()
    {
        _gestures.Feed(Press(3));
        _gestures.Feed(Press(12));
        Advance(100);
        _gestures.Feed(Press(12));
        Advance(50);

        AssertEvents(_gestures.Tick(), Release(3));
        AssertEvents(_gestures.Feed(Release(12)), Release(12), Tap(12));
    }

    [Fact]
    public void Keys_that_go_stale_together_are_released_in_the_order_they_were_pressed()
    {
        _gestures.Feed(Press(5));
        Advance(10);
        _gestures.Feed(Press(3));
        Advance(200);

        AssertEvents(_gestures.Tick(), Release(5), Release(3));
    }

    [Fact]
    public void A_hold_and_a_stale_release_can_come_out_of_the_same_pass()
    {
        _gestures.Feed(Press(12));
        _gestures.Feed(Press(3));
        for (var elapsed = 50; elapsed <= 850; elapsed += 50)
        {
            Advance(50);
            _gestures.Feed(Press(12));
            _gestures.Feed(Press(3));
        }

        // La 3 deja de llegar a los 850 ms; la 12 sigue hasta los 950.
        Advance(50);
        _gestures.Feed(Press(12));
        Advance(50);
        _gestures.Feed(Press(12));
        Advance(50);

        AssertEvents(_gestures.Tick(), Hold(12), Release(3));
    }

    [Fact]
    public void The_same_control_from_two_sources_is_two_keys()
    {
        var console = new InputEvent("console", "key.3", Gesture.Press);
        var other = new InputEvent("radio", "key.3", Gesture.Press);

        AssertEvents(_gestures.Feed(console), console);
        AssertEvents(_gestures.Feed(other), other);
        AssertEvents(_gestures.Feed(console with { Gesture = Gesture.Release }), console with { Gesture = Gesture.Release }, console with { Gesture = Gesture.Tap });
        Assert.Empty(_gestures.Feed(console with { Gesture = Gesture.Release }));
        AssertEvents(_gestures.Feed(other with { Gesture = Gesture.Release }), other with { Gesture = Gesture.Release }, other with { Gesture = Gesture.Tap });
    }

    [Fact]
    public void What_is_already_recognized_passes_through_and_follows_nothing()
    {
        var tap = new InputEvent("hotkeys", "key.3", Gesture.Tap);
        var hold = new InputEvent("hotkeys", "key.3", Gesture.Hold);

        AssertEvents(_gestures.Feed(tap), tap);
        AssertEvents(_gestures.Feed(hold), hold);
        Advance(5000);
        Assert.Empty(_gestures.Tick());
        Assert.Empty(_gestures.Feed(Release(3)));
    }

    [Fact]
    public void A_turn_passes_through_with_its_steps_and_does_not_disturb_a_held_key()
    {
        _gestures.Feed(Press(3));
        var turn = new InputEvent("console", "knob", Gesture.Turn, -2);

        AssertEvents(_gestures.Feed(turn), turn);
        AssertEvents(_gestures.Feed(Release(3)), Release(3), Tap(3));
    }

    [Fact]
    public void A_tick_with_nothing_held_is_empty_however_much_time_passes()
    {
        Assert.Empty(_gestures.Tick());
        Advance(60_000);
        Assert.Empty(_gestures.Tick());
    }

    [Fact]
    public void Stale_keys_are_not_remembered_so_a_late_release_is_ignored()
    {
        _gestures.Feed(Press(3));
        Advance(150);
        AssertEvents(_gestures.Tick(), Release(3));

        Assert.Empty(_gestures.Feed(Release(3)));
        Assert.Empty(_gestures.Tick());
    }

    [Fact]
    public async Task Feed_and_Tick_from_several_threads_do_not_corrupt_each_other()
    {
        var gestures = new Gestures(TimeProvider.System, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        using var stop = new CancellationTokenSource();
        var ticker = Task.Run(() =>
        {
            var seen = 0;
            while (!stop.IsCancellationRequested)
            {
                seen += gestures.Tick().Count;
            }

            return seen;
        });

        var keys = Enumerable.Range(1, 6).Select(key => Task.Run(() =>
        {
            for (var i = 0; i < 3000; i++)
            {
                Assert.Equal<InputEvent>([Press(key)], gestures.Feed(Press(key)));
                Assert.Empty(gestures.Feed(Press(key)));
                Assert.Equal<InputEvent>([Release(key), Tap(key)], gestures.Feed(Release(key)));
            }
        })).ToArray();

        await Task.WhenAll(keys);
        await stop.CancelAsync();

        Assert.Equal(0, await ticker);
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
