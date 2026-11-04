using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El emisor con la proyección real y la consola de verdad sobre un cable de memoria, como los arma Composition.Console (E9-5).</summary>
public class ConsoleFeedProjectionTests
{
    [Fact]
    public async Task On_connecting_the_cable_gets_the_real_screen_and_the_labels_of_the_main_page()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 20, 0, 0, TimeSpan.Zero));
        using var temp = new TempFolder();
        Library library = KeysRig.LoadLibrary(temp);
        await using var state = new StateStore();
        await using var log = new Log(temp.Combine("log"), clock);
        state.Update(s => s with { ActivePage = "grabacion", BasePage = "grabacion", MainPage = "grabacion" });
        var cable = new MemoryPipe();
        await using var console = new SerialConsole(_ => Task.FromResult<Stream>(cable), state, log, clock);
        await using var feed = new ConsoleFeed(state, () => library.Current, ConsoleProjection.Project, console.SendAsync, log, clock);
        console.ConsoleRebooted += feed.SendEverything;
        await console.StartAsync(CancellationToken.None);
        await Poll.Until(() => state.Current.Console.State == ConsoleLinkState.Connected);

        // Las pausas de 10 ms del emisor corren con el reloj falso: se lo empuja hasta que la vuelta termina, como ConsoleFeedRig.Round.
        Task round = feed.RoundAsync(CancellationToken.None);
        for (int spins = 0; !round.IsCompleted; spins++)
        {
            Assert.True(spins < 5000, "La vuelta del emisor no termina.");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await Task.Delay(1);
        }

        await round;

        ConsoleFrame frame = ConsoleProjection.Project(state.Current, library.Current, clock.GetUtcNow());
        string[] lines = cable.Written.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(ConsoleProtocol.ScreenStateLine(frame.Screen), lines);
        Assert.Contains(ConsoleProtocol.KeyLabelsLine(0, 5, frame.Key(4), frame.Key(5)), lines);
        Assert.Equal(new KeyFace(KeyRole.Marker, "SHORT", ""), frame.Key(4));
        Assert.Equal(new KeyFace(KeyRole.Marker, "TRAMO", "", Hold: true), frame.Key(11));
        Assert.Equal(ConsoleRecording.NoObs, frame.Screen.Recording);
    }
}
