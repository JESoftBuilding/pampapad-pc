using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>
/// El tope de 34 bytes de carga hacia la consola, para que la línea viaje por radio en una sola trama (04 §7, "Por radio cifra el
/// receptor"; plan de la 1.4.0, R-2, decisión 5). Cada mensaje se arma en su máximo.
/// </summary>
public class ConsolePayloadLimitTests
{
    [Fact]
    public void The_limit_is_34()
    {
        Assert.Equal(34, ConsoleProtocol.MaxConsolePayload);
    }

    [Fact]
    public void The_largest_screen_state_fits()
    {
        var frame = new ScreenFrame(
            Page: 255,
            Recording: ConsoleRecording.Recording,
            Flags: (ScreenFlags)0xFF,
            RecordedSec: int.MaxValue,
            PausedSec: int.MaxValue,
            SpanKey: 12,
            Canvas: 255,
            ActiveMask: 0xFFFF,
            OffMask: 0xFFFF,
            Alert: 255,
            LabelsVersion: 255,
            Flags2: (ScreenFlags2)0xFF,
            SpanSec: int.MaxValue);

        Fits(ConsoleProtocol.ScreenStateLine(frame));
    }

    [Fact]
    public void Key_labels_with_the_longest_labels_fit()
    {
        var face = new KeyFace(KeyRole.Danger, "ABCDEFGHIJKLMNOP", "QRSTUVWXYZABCDEF", Hold: true, Big: true);

        Fits(ConsoleProtocol.KeyLabelsLine(255, 11, face, face, 255));
    }

    [Theory]
    [InlineData(25)]
    [InlineData(ConsoleProtocol.MaxScreenTextLength)]
    [InlineData(60)]
    public void Screen_text_fits_with_25_letters_and_with_its_own_cap(int letters)
    {
        Fits(ConsoleProtocol.ScreenTextLine(5, 2, 255, new string('W', letters)));
    }

    [Fact]
    public void Settings_with_its_10_keys_fit()
    {
        var pairs = Enumerable.Range(1, ConsoleProtocol.MaxSettingsKeys).Select(k => ((byte)k, ushort.MaxValue)).ToList();

        Fits(ConsoleProtocol.SettingsLine(pairs, 0xFFFF));
    }

    [Fact]
    public void The_strip_with_8_leds_fits()
    {
        var leds = Enumerable.Repeat(new LedColor(255, 255, 255, (LedEffect)255), ConsoleProtocol.StripLeds).ToList();

        Fits(ConsoleProtocol.StripLine(leds));
    }

    [Fact]
    public void The_lights_line_fits_too()
    {
        Fits(ConsoleProtocol.LightLine(new LightCommand(5, 255, 255, 255, 255)));
    }

    [Fact]
    public void A_payload_of_34_goes_out_and_one_of_35_throws()
    {
        Fits(ConsoleProtocol.ToConsoleLine(0x4F, new byte[34]));

        Assert.Throws<ArgumentOutOfRangeException>(() => ConsoleProtocol.ToConsoleLine(0x4F, new byte[35]));
    }

    private static void Fits(string line)
    {
        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse(line, out var message));
        Assert.InRange(message.Payload.Length, 1, ConsoleProtocol.MaxConsolePayload);
    }
}
