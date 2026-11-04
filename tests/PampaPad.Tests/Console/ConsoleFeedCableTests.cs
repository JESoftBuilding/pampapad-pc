using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El emisor con su bucle de verdad sobre una SerialConsole y el cable en memoria: lo que llega a la placa (E9-4).</summary>
public class ConsoleFeedCableTests
{
    [Fact]
    public async Task On_connecting_the_board_gets_everything_and_after_unplugging_the_new_cable_gets_it_again()
    {
        await using var rig = new SerialConsoleRig();
        var first = rig.NextOpens();
        var second = rig.NextOpens();
        await using var feed = new ConsoleFeed(rig.Store, () => new Config(), (_, _, _) => ConsoleFeedRig.Example(), rig.Source.SendAsync, rig.Log, rig.Clock);
        await feed.StartAsync(CancellationToken.None);
        await rig.Source.StartAsync(CancellationToken.None);

        await rig.AdvanceUntil(() => Lines(first).Count >= 11);
        Assert.Equal(ConsoleSettingsWire.Line(null), Lines(first)[0]);
        Assert.Equal(0x40, ConsoleFeedRig.Type(Lines(first)[1]));

        first.Break();
        await Poll.Until(() => rig.Link == ConsoleLinkState.Waiting);
        var written = first.Written;
        rig.Clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(written, first.Written);

        await rig.AdvanceUntil(() => Lines(second).Count >= 11);
        Assert.Equal(ConsoleSettingsWire.Line(null), Lines(second)[0]);
    }

    [Fact]
    public async Task A_hello_that_just_booted_makes_it_send_everything_again()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await using var feed = new ConsoleFeed(rig.Store, () => new Config(), (_, _, _) => ConsoleFeedRig.Example(), rig.Source.SendAsync, rig.Log, rig.Clock);
        rig.Source.ConsoleRebooted += feed.SendEverything;
        await feed.StartAsync(CancellationToken.None);
        await rig.Source.StartAsync(CancellationToken.None);
        await rig.AdvanceUntil(() => Lines(cable).Count >= 11);

        // Con la huella propia: así los ajustes no se repiten por su cuenta y el segundo envío sale sólo por el reinicio.
        var own = ConsoleSettingsWire.Fingerprint(ConsoleSettingsWire.Pairs(null));
        cable.Feed("#311110" + Convert.ToBase64String(new byte[] { 1, 0, 1, 5, 0xC7, 0x80, (byte)own, (byte)(own >> 8) }) + "\n");

        await rig.AdvanceUntil(() => Lines(cable).Count(l => l == ConsoleSettingsWire.Line(null)) == 2);
    }

    private static List<string> Lines(MemoryPipe cable) => [.. cable.Written.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
}
