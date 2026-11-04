using PampaPad.Core;

namespace PampaPad.Tests;

// El saludo de la consola en ConsoleLink y sus líneas de texto en el registro (plan de la 9a, decisiones 13 y 14).
public class SerialConsoleHelloTests
{
    private const string Key3Down = "#301110BAAAAAAAAAA=\n";
    private const string Hello010 = "#311110AQABAMOAAAA=\n";

    [Fact]
    public async Task Without_a_hello_the_link_knows_nothing_of_the_firmware()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        var link = rig.Store.Current.Console;
        Assert.Equal(ConsoleLinkState.Connected, link.State);
        Assert.Null(link.Firmware);
        Assert.Null(link.Capabilities);
        Assert.Null(link.Energy);
        Assert.Null(link.SettingsFingerprint);
        Assert.Null(link.SettingsInSync);
    }

    [Fact]
    public async Task The_hello_leaves_its_firmware_capabilities_energy_and_fingerprint_in_the_link()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Hello010);

        await Poll.Until(() => rig.Store.Current.Console.Firmware is not null);
        var link = rig.Store.Current.Console;
        Assert.Equal("0.1.0", link.Firmware);
        Assert.Equal(0xC3, link.Capabilities);
        Assert.Equal(0, link.Energy);
        Assert.Equal(0, link.SettingsFingerprint);
        Assert.Equal("COM7", link.Port);
        Assert.Equal(0, rig.Source.UnknownMessages);
    }

    [Fact]
    public async Task Losing_the_link_forgets_the_hello()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        cable.Feed(Hello010);
        await Poll.Until(() => rig.Store.Current.Console.Firmware is not null);

        cable.Break();

        await Poll.Until(() => rig.Link == ConsoleLinkState.Waiting);
        Assert.Null(rig.Store.Current.Console.Firmware);
        Assert.Null(rig.Store.Current.Console.SettingsFingerprint);
    }

    [Fact]
    public async Task A_hello_that_did_not_just_boot_is_not_announced()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        var reboots = 0;
        rig.Source.ConsoleRebooted += () => Interlocked.Increment(ref reboots);
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Hello(1, 5, 0x00, 0x6BEB) + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(0, Volatile.Read(ref reboots));
        Assert.Equal(0x6BEB, rig.Store.Current.Console.SettingsFingerprint);
    }

    [Fact]
    public async Task A_short_hello_is_malformed_and_the_keys_keep_coming()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed("#311110AQAB\n" + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(1, rig.Source.MalformedLines);
        Assert.Null(rig.Store.Current.Console.Firmware);
    }

    [Fact]
    public async Task Text_lines_go_to_the_log_with_lights_as_a_warning_and_the_rest_as_info()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed("PampaPad consola 0.1.5: banda 812 us, celda 140 us, reinicio 1 POWERON\r\n"
                   + "luces: perdidos 2 con la fila llena, 0 sin contador, 0 copias que la radio no acepto\r\n\r\n" + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        var logged = Text(rig);
        Assert.Equal(2, logged.Count);
        Assert.Equal(LogLevel.Info, logged[0].Level);
        Assert.Equal("consola: PampaPad consola 0.1.5: banda 812 us, celda 140 us, reinicio 1 POWERON", logged[0].Message);
        Assert.Equal(LogLevel.Warning, logged[1].Level);
        Assert.StartsWith("consola: luces: perdidos 2", logged[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Past_20_text_lines_in_a_minute_the_rest_are_counted_and_said_when_the_minute_ends()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(string.Concat(Enumerable.Range(1, 25).Select(n => $"estado: {n}\n")) + Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());

        Assert.Equal(20, Text(rig).Count);
        Assert.DoesNotContain(Text(rig), e => e.Message == "consola: estado: 21");

        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        cable.Feed("estado: 26\n" + Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());

        var logged = Text(rig);
        Assert.Contains(logged, e => e.Message.Contains("5 líneas", StringComparison.Ordinal));
        Assert.Equal("consola: estado: 26", logged[^1].Message);
    }

    private static List<LogEntry> Text(SerialConsoleRig rig) =>
        [.. rig.Log.Recent().Where(e => e.Module == SerialConsole.SourceId && e.Message.StartsWith("consola:", StringComparison.Ordinal))];

    private static string Hello(int minor, int patch, int power, int fingerprint) =>
        "#311110" + Convert.ToBase64String(new byte[] { 1, 0, (byte)minor, (byte)patch, 0xC7, (byte)power, (byte)fingerprint, (byte)(fingerprint >> 8) }) + "\n";

    private static InputEvent Press(int key) => new(SerialConsole.SourceId, Controls.Key(key), Gesture.Press);
}
