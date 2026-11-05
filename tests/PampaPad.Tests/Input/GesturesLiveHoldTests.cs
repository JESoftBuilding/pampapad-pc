using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>holdMs en vivo (plan de la 9b, decisión 12): el umbral se lee de la biblioteca en cada gesto, como lo arma Composition, sin reiniciar.</summary>
public sealed class GesturesLiveHoldTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly CoreLibrary _library;
    private readonly Gestures _gestures;

    public GesturesLiveHoldTests()
    {
        Directory.CreateDirectory(_temp.Combine("biblioteca"));
        Write(1000);
        _library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
        _library.Load();
        _gestures = new Gestures(_clock, () => Gestures.HoldFrom(_library.Current.Console?.HoldMs), Gestures.DefaultFreshness);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_hold_ms_saved_after_starting_changes_the_threshold_of_the_next_gesture()
    {
        Assert.True(HeldFor(1000));

        Write(2000);
        _library.Load();

        Assert.False(HeldFor(1500));
        Assert.True(HeldFor(2000));
    }

    [Fact]
    public void Taking_hold_ms_out_goes_back_to_one_second()
    {
        Write(3000);
        _library.Load();
        Assert.False(HeldFor(1000));

        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), """{ "version": 1 }""");
        _library.Load();

        Assert.True(HeldFor(1000));
    }

    // Aprieta la tecla 3, la refresca cada 50 ms durante el tiempo pedido y la suelta: si salió un Hold en el camino.
    private bool HeldFor(int milliseconds)
    {
        var events = new List<InputEvent>(_gestures.Feed(new InputEvent("console", "key.3", Gesture.Press)));
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 50)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(50));
            events.AddRange(_gestures.Feed(new InputEvent("console", "key.3", Gesture.Press)));
            events.AddRange(_gestures.Tick());
        }

        events.AddRange(_gestures.Feed(new InputEvent("console", "key.3", Gesture.Release)));
        return events.Any(e => e.Gesture == Gesture.Hold);
    }

    private void Write(int holdMs) =>
        File.WriteAllText(_temp.Combine("biblioteca", "config.json"), $$"""{ "version": 1, "console": { "holdMs": {{holdMs}} } }""");
}
