using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El doble toque de la tecla del módulo (plan de la 1.3.0, decisiones 1 y 2): sólo donde se lo pide, con 300 ms de ventana.</summary>
public class GesturesDoubleTapTests
{
    private readonly FakeTimeProvider _clock = new();
    private bool _wanted = true;
    private readonly Gestures _gestures;

    public GesturesDoubleTapTests()
    {
        _gestures = new Gestures(_clock, () => TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(150), control => _wanted && control == Controls.ModuleKey);
    }

    [Fact]
    public void Without_double_tap_the_tap_comes_out_at_once()
    {
        _wanted = false;
        _gestures.Feed(Press());

        AssertEvents(_gestures.Feed(Release()), Release(), Tap());
    }

    [Fact]
    public void Another_control_keeps_its_tap_at_once()
    {
        var key = new InputEvent("console", "key.3", Gesture.Press);
        _gestures.Feed(key);

        AssertEvents(_gestures.Feed(key with { Gesture = Gesture.Release }), key with { Gesture = Gesture.Release }, key with { Gesture = Gesture.Tap });
    }

    [Fact]
    public void With_double_tap_the_tap_comes_out_at_300_ms_and_not_before()
    {
        _gestures.Feed(Press());
        AssertEvents(_gestures.Feed(Release()), Release());

        Advance(299);
        Assert.Empty(_gestures.Tick());

        Advance(1);
        AssertEvents(_gestures.Tick(), Tap());
        Assert.Empty(_gestures.Tick());
    }

    [Fact]
    public void Two_taps_are_one_double_tap_and_no_tap()
    {
        _gestures.Feed(Press());
        _gestures.Feed(Release());
        Advance(200);

        AssertEvents(_gestures.Feed(Press()), Press());
        Advance(50);
        AssertEvents(_gestures.Feed(Release()), Release(), DoubleTap());

        Advance(1000);
        Assert.Empty(_gestures.Tick());
    }

    [Fact]
    public void A_held_second_press_is_a_hold_and_the_first_tap_is_lost()
    {
        _gestures.Feed(Press());
        _gestures.Feed(Release());
        Advance(100);
        _gestures.Feed(Press());
        KeepDown(950);
        Advance(50);

        AssertEvents(_gestures.Tick(), Hold());
        AssertEvents(_gestures.Feed(Release()), Release());
        Advance(1000);
        Assert.Empty(_gestures.Tick());
    }

    [Fact]
    public void Three_taps_are_a_double_tap_and_a_new_first_tap()
    {
        for (int i = 0; i < 2; i++)
        {
            _gestures.Feed(Press());
            _gestures.Feed(Release());
            Advance(100);
        }

        _gestures.Feed(Press());
        AssertEvents(_gestures.Feed(Release()), Release());

        Advance(300);
        AssertEvents(_gestures.Tick(), Tap());
    }

    [Fact]
    public void A_second_press_that_goes_stale_is_only_a_release()
    {
        _gestures.Feed(Press());
        _gestures.Feed(Release());
        Advance(100);
        _gestures.Feed(Press());
        Advance(150);

        AssertEvents(_gestures.Tick(), Release());
        Advance(1000);
        Assert.Empty(_gestures.Tick());
    }

    [Fact]
    public void Taking_double_tap_away_with_a_saved_tap_lets_the_tap_out_once()
    {
        _gestures.Feed(Press());
        _gestures.Feed(Release());
        Advance(100);
        _wanted = false;

        AssertEvents(_gestures.Tick(), Tap());
        AssertEvents(_gestures.Feed(Press()), Press());
        AssertEvents(_gestures.Feed(Release()), Release(), Tap());
        Advance(1000);
        Assert.Empty(_gestures.Tick());
    }

    // Revisión de la tanda 1: sacar doubleTap entre el segundo apretar y su soltar no pierde los dos toques; sale un toque.
    [Fact]
    public void Taking_double_tap_away_during_the_second_press_lets_a_tap_out()
    {
        _gestures.Feed(Press());
        _gestures.Feed(Release());
        Advance(100);
        _gestures.Feed(Press());
        _wanted = false;

        AssertEvents(_gestures.Feed(Release()), Release(), Tap());
        Advance(1000);
        Assert.Empty(_gestures.Tick());
    }

    [Fact]
    public void A_press_at_300_ms_is_too_late_the_saved_tap_comes_out_before_it()
    {
        _gestures.Feed(Press());
        _gestures.Feed(Release());
        Advance(300);

        AssertEvents(_gestures.Feed(Press()), Tap(), Press());
    }

    private void Advance(int milliseconds) => _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));

    private void KeepDown(int milliseconds)
    {
        for (var elapsed = 50; elapsed <= milliseconds; elapsed += 50)
        {
            Advance(50);
            _gestures.Feed(Press());
        }
    }

    private static InputEvent Press() => new("console", Controls.ModuleKey, Gesture.Press);

    private static InputEvent Release() => new("console", Controls.ModuleKey, Gesture.Release);

    private static InputEvent Tap() => new("console", Controls.ModuleKey, Gesture.Tap);

    private static InputEvent DoubleTap() => new("console", Controls.ModuleKey, Gesture.DoubleTap);

    private static InputEvent Hold() => new("console", Controls.ModuleKey, Gesture.Hold);

    private static void AssertEvents(IReadOnlyList<InputEvent> actual, params InputEvent[] expected) =>
        Assert.Equal<InputEvent>(expected, actual);
}
