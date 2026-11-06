using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El emisor con el receptor: callado mientras la consola está sin señal y entero cuando vuelve (plan de la 1.4.0, R-2, decisión 4).</summary>
public class ConsoleFeedReceiverTests
{
    private const byte Settings = 0x44;
    private const byte Screen = 0x40;
    private const byte Labels = 0x41;
    private const byte Text = 0x42;
    private const byte Strip = 0x46;

    [Fact]
    public async Task Nothing_goes_out_while_the_console_is_without_signal()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        Hears(rig, true);
        await rig.Round();
        Assert.NotEmpty(rig.Sent);
        rig.Clear();

        LosesSignal(rig);
        await rig.Run(TimeSpan.FromSeconds(10));

        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task Without_signal_from_the_start_nothing_goes_out()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect();
        Hears(rig, false);

        await rig.Run(TimeSpan.FromSeconds(5));

        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task When_the_console_comes_back_everything_goes_out_as_on_connecting_and_the_fingerprint_is_checked_again()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        Hears(rig, true);
        await rig.Round();
        await rig.Run(TimeSpan.FromSeconds(1));
        Assert.True(rig.Store.Current.Console.SettingsInSync);
        LosesSignal(rig);
        await rig.Run(TimeSpan.FromSeconds(3));
        rig.Clear();

        Hears(rig, true);
        rig.Clock.Advance(ConsoleFeedRig.Tick);
        await rig.Round();

        var lines = rig.Sent.Select(s => s.Line).ToList();
        Assert.Equal(new byte[] { Settings, Screen, Labels, Labels, Labels, Labels, Labels, Labels, Text, Text, Strip }, lines.Select(ConsoleFeedRig.Type));

        // Sin el Hello de vuelta la huella no se sabe y los ajustes se repiten cada 2 s; con la propia, paran.
        await rig.Run(TimeSpan.FromSeconds(4));
        Assert.Equal(3, rig.Sent.Count(s => ConsoleFeedRig.Type(s.Line) == Settings));
        Assert.Null(rig.Store.Current.Console.SettingsInSync);

        rig.Hello(rig.OwnFingerprint);
        await rig.Run(TimeSpan.FromSeconds(6));
        Assert.Equal(3, rig.Sent.Count(s => ConsoleFeedRig.Type(s.Line) == Settings));
        Assert.True(rig.Store.Current.Console.SettingsInSync);
    }

    [Fact]
    public async Task With_the_receiver_hearing_the_console_it_sends_as_by_cable()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        Hears(rig, true);
        await rig.Round();
        rig.Clear();

        await rig.Run(TimeSpan.FromSeconds(1));

        Assert.Equal(2, rig.Sent.Count(s => ConsoleFeedRig.Type(s.Line) == Screen));
    }

    private static void Hears(ConsoleFeedRig rig, bool hears) =>
        rig.Store.Update(s => s with { Console = s.Console with { Receiver = new ReceiverLink("0.1.0", hears) } });

    // Lo que hace SerialConsole al pasar a sin señal: el receptor en falso y el saludo de la consola olvidado.
    private static void LosesSignal(ConsoleFeedRig rig) =>
        rig.Store.Update(s => s with
        {
            Console = s.Console with
            {
                Receiver = new ReceiverLink("0.1.0", false),
                Firmware = null,
                Capabilities = null,
                Energy = null,
                SettingsFingerprint = null,
                SettingsInSync = null,
            },
        });
}
