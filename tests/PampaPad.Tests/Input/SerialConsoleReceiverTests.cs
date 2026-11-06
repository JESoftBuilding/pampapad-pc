using PampaPad.Core;

namespace PampaPad.Tests;

// El saludo del receptor, ReceiverHello 0x32, y la "consola sin señal" en el enlace (plan de la 1.4.0, R-2, decisiones 1 a 3).
public class SerialConsoleReceiverTests
{
    private const string Key3Down = "#301110BAAAAAAAAAA=\n";
    private const string Hello010 = "#311110AQABAMOAAAA=\n";

    [Fact]
    public void The_spec_vector_is_0_1_0_without_the_console()
    {
        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse("#321010AAEAAAA=", out var message));
        Assert.Equal(ConsoleProtocol.ReceiverHelloType, message.Type);

        Assert.True(ConsoleProtocol.TryDecodeReceiverHello(message.Payload, out var hello));
        Assert.Equal(new ReceiverHello("0.1.0", false), hello);
    }

    [Fact]
    public void Bit_0_of_the_status_says_it_hears_the_console_and_the_rest_of_the_byte_does_not()
    {
        Assert.True(ConsoleProtocol.TryDecodeReceiverHello([0, 2, 7, 0x01, 0], out var hears));
        Assert.Equal(new ReceiverHello("0.2.7", true), hears);

        Assert.True(ConsoleProtocol.TryDecodeReceiverHello([1, 0, 0, 0xFE, 9], out var deaf));
        Assert.Equal(new ReceiverHello("1.0.0", false), deaf);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    public void Another_length_does_not_decode(int length)
    {
        Assert.False(ConsoleProtocol.TryDecodeReceiverHello(new byte[length], out _));
    }

    [Fact]
    public async Task The_receiver_hello_is_no_longer_an_unknown_type_and_writes_the_receiver()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Receiver(hears: true) + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(0, rig.Source.UnknownMessages);
        Assert.Equal(0, rig.Source.MalformedLines);
        Assert.Equal(new ReceiverLink("0.1.0", true), rig.Store.Current.Console.Receiver);
        Assert.Equal(ConsoleLinkState.Connected, rig.Link);
        Assert.False(rig.Store.Current.Console.WithoutSignal());
    }

    [Fact]
    public async Task A_receiver_hello_of_another_length_is_malformed()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed("#321010AAEAAA==\n" + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.Equal(1, rig.Source.MalformedLines);
        Assert.Equal(0, rig.Source.UnknownMessages);
        Assert.Null(rig.Store.Current.Console.Receiver);
    }

    [Fact]
    public async Task Without_signal_the_console_hello_is_forgotten_and_its_next_hello_fills_it_again()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        cable.Feed(Receiver(hears: true) + Hello010 + Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());
        rig.Store.Update(s => s with { Console = s.Console with { SettingsInSync = true } });

        cable.Feed(Receiver(hears: false) + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        var silent = rig.Store.Current.Console;
        Assert.True(silent.WithoutSignal());
        Assert.Equal(ConsoleLinkState.Connected, silent.State);
        Assert.Equal(new ReceiverLink("0.1.0", false), silent.Receiver);
        Assert.Null(silent.Firmware);
        Assert.Null(silent.Capabilities);
        Assert.Null(silent.Energy);
        Assert.Null(silent.SettingsFingerprint);
        Assert.Null(silent.SettingsInSync);

        cable.Feed(Hello010 + Receiver(hears: true) + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        var back = rig.Store.Current.Console;
        Assert.False(back.WithoutSignal());
        Assert.Equal("0.1.0", back.Firmware);
        Assert.Equal(0xC3, back.Capabilities);
        Assert.Equal(0, back.SettingsFingerprint);
    }

    [Fact]
    public async Task The_console_hello_then_a_receiver_hello_without_it_leaves_it_without_signal()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Hello010 + Receiver(hears: false) + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        var link = rig.Store.Current.Console;
        Assert.True(link.WithoutSignal());
        Assert.Null(link.Firmware);
        Assert.Null(link.SettingsFingerprint);
    }

    [Fact]
    public async Task By_cable_without_a_receiver_nothing_changes()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Hello010 + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        var link = rig.Store.Current.Console;
        Assert.Null(link.Receiver);
        Assert.False(link.WithoutSignal());
        Assert.Equal("0.1.0", link.Firmware);
        Assert.DoesNotContain(rig.Log.Recent(), e => e.Message.Contains("receptor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Losing_the_link_forgets_the_receiver()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        cable.Feed(Receiver(hears: false) + Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());

        cable.Break();

        await Poll.Until(() => rig.Link == ConsoleLinkState.Waiting);
        Assert.Null(rig.Store.Current.Console.Receiver);
        Assert.False(rig.Store.Current.Console.WithoutSignal());
    }

    [Fact]
    public async Task The_log_says_the_first_hello_and_then_only_each_change_of_hearing_the_console()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);

        cable.Feed(Receiver(hears: true) + Receiver(hears: true) + Receiver(hears: false) + Receiver(hears: false)
                   + Receiver(hears: true) + Receiver(hears: true) + Key3Down);

        Assert.Equal(Press(3), await rig.NextEvent());
        var logged = rig.Log.Recent().Where(e => e.Module == SerialConsole.SourceId && e.Message.Contains("receptor", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, logged.Count);
        Assert.StartsWith("El receptor saluda: firmware 0.1.0", logged[0].Message, StringComparison.Ordinal);
        Assert.Contains("sin señal", logged[1].Message, StringComparison.Ordinal);
        Assert.Contains("volvió a escuchar", logged[2].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_lights_still_go_out_with_the_console_without_signal()
    {
        await using var rig = new SerialConsoleRig();
        var cable = rig.NextOpens();
        await rig.Source.StartAsync(CancellationToken.None);
        cable.Feed(Receiver(hears: false) + Key3Down);
        Assert.Equal(Press(3), await rig.NextEvent());
        Assert.True(rig.Store.Current.Console.WithoutSignal());

        var color = ConsoleProtocol.LightLine(new LightCommand(1, 0x6D, 0x5D, 0xF6, 0));
        var brightness = Lights.AdjustBrightnessLine(-5);
        Assert.True((await rig.Source.SendAsync(color, CancellationToken.None)).Ok);
        Assert.True((await rig.Source.SendAsync(brightness, CancellationToken.None)).Ok);

        Assert.Equal(color + "\n" + brightness + "\n", cable.Written);
    }

    private static string Receiver(bool hears) =>
        "#321010" + Convert.ToBase64String(new byte[] { 0, 1, 0, (byte)(hears ? 1 : 0), 0 }) + "\n";

    private static InputEvent Press(int key) => new(SerialConsole.SourceId, Controls.Key(key), Gesture.Press);
}
