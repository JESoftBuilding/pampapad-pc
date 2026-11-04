using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>El emisor de la consola con reloj falso: los ritmos de la decisión 5, el tope de la 6 y la huella de la 7 (plan de la 9a, E9-4).</summary>
public class ConsoleFeedTests
{
    private const byte Settings = 0x44;
    private const byte Screen = 0x40;
    private const byte Labels = 0x41;
    private const byte Text = 0x42;
    private const byte Strip = 0x46;

    [Fact]
    public async Task On_connecting_everything_goes_out_in_order()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect();

        await rig.Round();

        var lines = rig.Sent.Select(s => s.Line).ToList();
        Assert.Equal(new byte[] { Settings, Screen, Labels, Labels, Labels, Labels, Labels, Labels, Text, Text, Strip }, lines.Select(ConsoleFeedRig.Type));
        Assert.Equal(ConsoleSettingsWire.Line(null), lines[0]);
        Assert.Equal(ConsoleProtocol.ScreenStateLine(rig.Frame.Screen), lines[1]);
        Assert.Equal(new[] { 1, 3, 5, 7, 9, 11 }, lines[2..8].Select(l => (int)ConsoleFeedRig.Payload(l)[1]));
        Assert.Equal(ConsoleProtocol.KeyLabelsLine(0, 11, rig.Frame.Keys[10], rig.Frame.Keys[11]), lines[7]);
        Assert.Equal(ConsoleProtocol.ScreenTextLine(0, 0, 1, "CAMARA"), lines[8]);
        Assert.Equal(ConsoleProtocol.ScreenTextLine(1, 0, 1, ""), lines[9]);
        Assert.Equal(ConsoleProtocol.StripLine(rig.Frame.Strip), lines[10]);
    }

    [Fact]
    public async Task Never_more_than_7_lines_go_out_without_a_10_ms_pause()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect();

        await rig.Round();

        var sent = rig.Sent;
        Assert.Equal(11, sent.Count);
        var run = 1;
        for (var i = 1; i < sent.Count; i++)
        {
            run = sent[i].At - sent[i - 1].At >= TimeSpan.FromMilliseconds(10) ? 1 : run + 1;
            Assert.True(run <= 7, $"La línea {i + 1} es la {run}ª seguida sin pausa.");
        }
    }

    [Fact]
    public async Task A_quiet_minute_sends_120_screens_60_labels_30_of_each_text_60_strips_and_nothing_else()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        await rig.Round();
        rig.Clear();

        await rig.Run(TimeSpan.FromMinutes(1));

        var lines = rig.Sent.Select(s => s.Line).ToList();
        Assert.Equal(120, lines.Count(l => ConsoleFeedRig.Type(l) == Screen));
        Assert.Equal(60, lines.Count(l => ConsoleFeedRig.Type(l) == Labels));
        Assert.Equal(30, lines.Count(l => ConsoleFeedRig.Type(l) == Text && ConsoleFeedRig.Payload(l)[0] == 0));
        Assert.Equal(30, lines.Count(l => ConsoleFeedRig.Type(l) == Text && ConsoleFeedRig.Payload(l)[0] == 1));
        Assert.Equal(60, lines.Count(l => ConsoleFeedRig.Type(l) == Strip));
        Assert.Equal(300, lines.Count);

        // Las etiquetas de fondo rotan por las seis parejas.
        var firsts = lines.Where(l => ConsoleFeedRig.Type(l) == Labels).Take(7).Select(l => (int)ConsoleFeedRig.Payload(l)[1]);
        Assert.Equal(new[] { 1, 3, 5, 7, 9, 11, 1 }, firsts);
    }

    [Fact]
    public async Task With_the_same_fingerprint_settings_stop_and_the_link_says_in_sync()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect();
        await rig.Round();

        rig.Hello(rig.OwnFingerprint);
        await rig.Run(TimeSpan.FromSeconds(10));

        Assert.Single(rig.Sent, s => ConsoleFeedRig.Type(s.Line) == Settings);
        Assert.True(rig.Store.Current.Console.SettingsInSync);
    }

    [Fact]
    public async Task With_another_fingerprint_settings_go_every_2_seconds_and_the_log_says_it_once()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(0x1234);
        await rig.Round();

        await rig.Run(TimeSpan.FromSeconds(20));

        // La de la conexión y una cada 2 s.
        Assert.Equal(11, rig.Sent.Count(s => ConsoleFeedRig.Type(s.Line) == Settings));
        Assert.False(rig.Store.Current.Console.SettingsInSync);
        Assert.Single(rig.Log.Recent(), e => e.Module == ConsoleFeed.Module && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Without_a_hello_the_settings_are_not_confirmed_and_the_link_does_not_say()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect();
        await rig.Round();

        await rig.Run(TimeSpan.FromSeconds(4));

        Assert.Equal(3, rig.Sent.Count(s => ConsoleFeedRig.Type(s.Line) == Settings));
        Assert.Null(rig.Store.Current.Console.SettingsInSync);
    }

    [Fact]
    public async Task A_page_change_sends_its_screen_state_before_its_six_labels()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        await rig.Round();
        rig.Clear();

        rig.Frame = ConsoleFeedRig.Example(page: 1);
        rig.Clock.Advance(ConsoleFeedRig.Tick);
        await rig.Round();

        var lines = rig.Sent.Select(s => s.Line).ToList();
        Assert.Equal(new byte[] { Screen, Labels, Labels, Labels, Labels, Labels, Labels }, lines.Select(ConsoleFeedRig.Type));
        Assert.Equal(1, ConsoleFeedRig.Payload(lines[0])[0]);
        Assert.All(lines[1..], l => Assert.Equal(1, ConsoleFeedRig.Payload(l)[0]));
    }

    [Fact]
    public async Task Two_changes_before_a_round_send_one_screen_state_with_the_last()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        await rig.Round();
        rig.Clear();

        rig.Frame = rig.Frame with { Screen = rig.Frame.Screen with { Recording = ConsoleRecording.Starting } };
        rig.Frame = rig.Frame with { Screen = rig.Frame.Screen with { Recording = ConsoleRecording.Recording } };
        rig.Clock.Advance(ConsoleFeedRig.Tick);
        await rig.Round();

        var line = Assert.Single(rig.Sent).Line;
        Assert.Equal((byte)ConsoleRecording.Recording, ConsoleFeedRig.Payload(line)[1]);
    }

    [Fact]
    public async Task A_changed_text_or_strip_goes_out_in_the_next_round()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        await rig.Round();
        rig.Clear();

        rig.Frame = rig.Frame with { SmallLine = new ConsoleText("MIC MUDO"), Strip = [.. Enumerable.Repeat(new LedColor(255, 0, 0), 8)] };
        rig.Clock.Advance(ConsoleFeedRig.Tick);
        await rig.Round();

        Assert.Equal(
            [ConsoleProtocol.ScreenTextLine(1, 0, 1, "MIC MUDO"), ConsoleProtocol.StripLine(rig.Frame.Strip)],
            rig.Sent.Select(s => s.Line));
    }

    [Fact]
    public async Task Unplugged_it_writes_and_logs_nothing_and_on_coming_back_it_sends_everything()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        await rig.Round();
        rig.Disconnect();
        rig.Clear();
        var logged = rig.Log.Recent().Count;

        await rig.Run(TimeSpan.FromSeconds(5));

        Assert.Empty(rig.Sent);
        Assert.Equal(logged, rig.Log.Recent().Count);

        rig.Connect();
        await rig.Run(ConsoleFeedRig.Tick);
        Assert.Equal(11, rig.Sent.Count);
        Assert.Equal(Settings, ConsoleFeedRig.Type(rig.Sent[0].Line));
    }

    [Fact]
    public async Task A_failed_write_keeps_it_quiet_without_logging_until_the_console_connects_again()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Answer = _ => Task.FromResult(Result.Fail(ErrorCodes.ConsoleDisconnected, "No se pudo escribir en la consola."));
        rig.Connect(rig.OwnFingerprint);
        var logged = rig.Log.Recent().Count;

        await rig.Run(TimeSpan.FromSeconds(1));

        Assert.Single(rig.Sent);
        Assert.Equal(logged, rig.Log.Recent().Count);

        rig.Answer = _ => Task.FromResult(Result.Success);
        rig.Disconnect();
        await rig.Run(ConsoleFeedRig.Tick);
        rig.Connect(rig.OwnFingerprint);
        await rig.Run(ConsoleFeedRig.Tick);
        Assert.Equal(1 + 11, rig.Sent.Count);
    }

    // Revisión de la tanda 1 de la 9a (should): la consola borra las teclas al cambiar de página (ConsoleModel.h:83), así que una pareja
    // que se ve igual en las dos páginas también tiene que viajar; si no, queda en blanco hasta que la rotación la alcance.
    [Fact]
    public async Task A_page_change_sends_all_six_labels_even_when_they_look_the_same_as_the_old_page()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        await rig.Round();
        rig.Clear();

        rig.Frame = rig.Frame with { Screen = rig.Frame.Screen with { Page = 1 } };
        rig.Clock.Advance(ConsoleFeedRig.Tick);
        await rig.Round();

        Assert.Equal(new byte[] { Screen, Labels, Labels, Labels, Labels, Labels, Labels }, rig.Sent.Select(s => ConsoleFeedRig.Type(s.Line)));
    }

    // Revisión de la tanda 1 de la 9a (should): con el enlace todavía conectado, una escritura que falló no puede callar al emisor para siempre.
    [Fact]
    public async Task After_a_failed_write_with_the_link_still_connected_it_sends_everything_again_two_seconds_later()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Answer = _ => Task.FromResult(Result.Fail(ErrorCodes.ConsoleDisconnected, "No se pudo escribir en la consola."));
        rig.Connect(rig.OwnFingerprint);
        await rig.Run(ConsoleFeedRig.Tick);
        Assert.Single(rig.Sent);

        rig.Answer = _ => Task.FromResult(Result.Success);
        await rig.Run(TimeSpan.FromSeconds(2));

        Assert.True(rig.Sent.Count >= 1 + 11, $"Salieron {rig.Sent.Count} líneas");
        Assert.Equal(Settings, ConsoleFeedRig.Type(rig.Sent[1].Line));
    }

    [Fact]
    public async Task Just_booted_sends_everything_again()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        await rig.Round();
        rig.Clear();

        rig.Feed.SendEverything();
        rig.Clock.Advance(ConsoleFeedRig.Tick);
        await rig.Round();

        Assert.Equal(11, rig.Sent.Count);
        Assert.Equal(Settings, ConsoleFeedRig.Type(rig.Sent[0].Line));
    }

    [Fact]
    public async Task A_write_that_takes_500_ms_delays_the_round_and_the_next_one_still_goes()
    {
        await using var rig = new ConsoleFeedRig();
        rig.Connect(rig.OwnFingerprint);
        await rig.Round();
        rig.Clear();
        var slow = true;
        rig.Answer = async _ =>
        {
            if (slow)
            {
                slow = false;
                await Task.Delay(TimeSpan.FromMilliseconds(500), rig.Clock);
            }

            return Result.Success;
        };

        await rig.Run(TimeSpan.FromSeconds(1));

        Assert.Contains(rig.Sent, s => ConsoleFeedRig.Type(s.Line) == Screen);
        Assert.True(rig.Sent.Count >= 3);
    }
}
