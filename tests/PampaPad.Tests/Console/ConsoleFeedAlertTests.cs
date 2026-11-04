using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El aviso por el cable (plan de la 9b, decisión 10): primero el número, después los cuatro textos dos veces, y uno cada medio segundo.</summary>
public class ConsoleFeedAlertTests
{
    private const byte Screen = 0x40;
    private const byte Text = 0x42;

    private static readonly ConsoleAlert Mic = new(2, new("MIC MUDO", 2), new("ESTAS GRABANDO"), new(""), new("MIC: ABRIR   CLICK: YA SE"));
    private static readonly ConsoleAlert Pause = new(3, new("EN PAUSA", 1), new("HACE 3:00"), new(""), new("NO SE GRABA NADA"));

    private static ConsoleFrame With(ConsoleAlert? alert)
    {
        ConsoleFrame frame = ConsoleFeedRig.Example();
        return frame with { Screen = frame.Screen with { Alert = alert?.Number ?? 0 }, Alert = alert };
    }

    // Los textos del aviso: destinos 2 a 5. Los de la línea de escena y el renglón chico son 0 y 1.
    private static List<byte[]> AlertTexts(ConsoleFeedRig rig) =>
        [.. rig.Sent.Select(s => s.Line).Where(l => ConsoleFeedRig.Type(l) == Text).Select(ConsoleFeedRig.Payload).Where(p => p[0] >= 2)];

    private static async Task<ConsoleFeedRig> QuietAsync()
    {
        var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        await rig.Round();
        rig.Clear();
        return rig;
    }

    [Fact]
    public async Task On_appearing_the_number_goes_first_and_then_the_four_texts_twice_with_the_number_as_reference()
    {
        await using ConsoleFeedRig rig = await QuietAsync();
        rig.Frame = With(Mic);

        await rig.Run(ConsoleFeedRig.Tick);

        List<string> lines = [.. rig.Sent.Select(s => s.Line)];
        Assert.Equal(Screen, ConsoleFeedRig.Type(lines[0]));
        Assert.Equal(2, ConsoleFeedRig.Payload(lines[0])[15]);
        string[] texts = [.. Enumerable.Range(2, 4).Select(t => ConsoleProtocol.ScreenTextLine(t, Mic.Text(t).Tone, 2, Mic.Text(t).Text))];
        Assert.Equal([.. texts, .. texts], lines.Where(l => ConsoleFeedRig.Type(l) == Text));
        Assert.Contains("#421011AgICCE1JQyBNVURP", lines);
        Assert.Contains("#421011BQACGU1JQzogQUJSSVIgICBDTElDSzogWUEgU0U=", lines);
    }

    [Fact]
    public async Task Ten_seconds_of_a_quiet_alert_send_twenty_texts_rotating_and_no_more()
    {
        await using ConsoleFeedRig rig = await QuietAsync();
        rig.Frame = With(Mic);
        await rig.Run(ConsoleFeedRig.Tick);
        rig.Clear();

        await rig.Run(TimeSpan.FromSeconds(10));

        List<byte[]> texts = AlertTexts(rig);
        Assert.Equal(20, texts.Count);
        Assert.Equal(new byte[] { 2, 3, 4, 5, 2, 3, 4, 5 }, texts.Take(8).Select(p => p[0]));
        Assert.All(texts, p => Assert.Equal(2, p[2]));
    }

    [Fact]
    public async Task Changing_to_another_alert_sends_its_four_texts_again_and_a_changed_text_goes_in_the_next_round()
    {
        await using ConsoleFeedRig rig = await QuietAsync();
        rig.Frame = With(Mic);
        await rig.Run(ConsoleFeedRig.Tick);
        rig.Clear();

        rig.Frame = With(Pause);
        await rig.Run(ConsoleFeedRig.Tick);
        Assert.Equal(3, ConsoleFeedRig.Payload(rig.Sent[0].Line)[15]);
        Assert.Equal(8, AlertTexts(rig).Count(p => p[2] == 3));
        rig.Clear();

        rig.Frame = With(Pause with { Line1 = new("HACE 3:01") });
        await rig.Run(ConsoleFeedRig.Tick);
        Assert.Contains(ConsoleProtocol.ScreenTextLine(3, 0, 3, "HACE 3:01"), rig.Sent.Select(s => s.Line));
    }

    [Fact]
    public async Task On_going_off_byte_15_is_zero_and_no_more_alert_texts_go_out()
    {
        await using ConsoleFeedRig rig = await QuietAsync();
        rig.Frame = With(Mic);
        await rig.Run(TimeSpan.FromSeconds(1));
        rig.Clear();

        rig.Frame = With(null);
        await rig.Run(TimeSpan.FromSeconds(3));

        Assert.Equal(0, ConsoleFeedRig.Payload(rig.Sent[0].Line)[15]);
        Assert.Empty(AlertTexts(rig));
    }

    [Fact]
    public async Task Sending_everything_includes_the_texts_of_the_alert_on_view()
    {
        await using ConsoleFeedRig rig = await QuietAsync();
        rig.Frame = With(Mic);
        await rig.Run(TimeSpan.FromSeconds(1));
        rig.Clear();

        rig.Feed.SendEverything();
        await rig.Run(ConsoleFeedRig.Tick);

        Assert.Equal(8, AlertTexts(rig).Count);
    }
}
