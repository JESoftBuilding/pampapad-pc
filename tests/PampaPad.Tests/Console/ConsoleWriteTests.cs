using System.Text;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Los mensajes de la PC a la consola contra los vectores del firmware 0.1.5 (test_consolewire/test_main.cpp:48-133), byte a byte (E9-3).</summary>
public class ConsoleWriteTests
{
    private static readonly (byte Key, ushort Value)[] Defaults = [(1, 80), (2, 15), (3, 60), (4, 300), (5, 0), (6, 1000), (7, 3), (8, 25)];

    [Fact]
    public void The_screen_state_vector_is_recording_2052_seconds_with_key_1_active()
    {
        var frame = new ScreenFrame(
            Page: 0,
            Recording: ConsoleRecording.Recording,
            Flags: ScreenFlags.PreflightRan,
            RecordedSec: 2052,
            ActiveMask: 0x0001,
            Flags2: ScreenFlags2.KeepAwake);

        Assert.Equal("#401011AAMgBAgAAAAAZAABAAAAAAEBAAA=", ConsoleProtocol.ScreenStateLine(frame));
    }

    [Fact]
    public void The_screen_state_puts_each_number_little_endian_in_its_byte()
    {
        var frame = new ScreenFrame(
            Page: 2,
            Recording: ConsoleRecording.Paused,
            Flags: ScreenFlags.MicMuted | ScreenFlags.SpanOpen,
            RecordedSec: 0x123456,
            PausedSec: 0x0708,
            SpanKey: 12,
            Canvas: 0x80 | 75,
            ActiveMask: 0x0A0B,
            OffMask: 0x0C0D,
            Alert: 4,
            LabelsVersion: 9,
            Flags2: ScreenFlags2.KeepAwake | ScreenFlags2.AlertSound,
            SpanSec: 0x0E0F);

        Assert.Equal(
            new byte[] { 2, 4, 0x11, 0x56, 0x34, 0x12, 0x08, 0x07, 12, 0xCB, 0, 0x0B, 0x0A, 0x0D, 0x0C, 4, 9, 0x05, 0x0F, 0x0E },
            Payload(ConsoleProtocol.ScreenStateLine(frame), 0x40));
    }

    [Fact]
    public void Seconds_past_what_fits_stay_at_the_top_and_do_not_wrap()
    {
        var frame = new ScreenFrame(0, ConsoleRecording.Recording, RecordedSec: 0x1000000 + 5, PausedSec: 70000, SpanSec: -3);

        var payload = Payload(ConsoleProtocol.ScreenStateLine(frame), 0x40);

        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF }, payload[3..6]);
        Assert.Equal(new byte[] { 0xFF, 0xFF }, payload[6..8]);
        Assert.Equal(new byte[] { 0, 0 }, payload[18..20]);
    }

    [Fact]
    public void The_key_labels_vector_is_camara_as_a_scene_and_short_as_a_marker()
    {
        var line = ConsoleProtocol.KeyLabelsLine(0, 1, new KeyFace(KeyRole.Scene, "CAMARA", ""), new KeyFace(KeyRole.Marker, "SHORT", ""));

        Assert.Equal("#411011AAEBAkNBTUFSQQAAAAAAAANTSE9SVAAAAAAAAAA=", line);
    }

    [Fact]
    public void The_style_is_the_role_with_hold_in_bit_6_and_big_in_bit_7()
    {
        var line = ConsoleProtocol.KeyLabelsLine(3, 11, new KeyFace(KeyRole.Danger, "PARAR", "", Hold: true), new KeyFace(KeyRole.Normal, "MIC", "", Big: true));

        var payload = Payload(line, 0x41);
        Assert.Equal(new byte[] { 3, 11, 1 }, payload[..3]);
        Assert.Equal(0x44, payload[3]);
        Assert.Equal(0x81, payload[16]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(12)]
    [InlineData(13)]
    public void Key_labels_only_start_on_an_odd_key_from_1_to_11(int first)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConsoleProtocol.KeyLabelsLine(0, first, KeyFace.Empty, KeyFace.Empty));
    }

    [Fact]
    public void Label_letters_go_in_ascii_cut_at_6_and_padded_with_zeros()
    {
        var payload = Payload(ConsoleProtocol.KeyLabelsLine(0, 1, new KeyFace(KeyRole.Normal, "CÁMARAS", "ñ"), KeyFace.Empty), 0x41);

        Assert.Equal("CAMARA"u8.ToArray(), payload[4..10]);
        Assert.Equal(new byte[] { (byte)'n', 0, 0, 0, 0, 0 }, payload[10..16]);
        Assert.Equal(new byte[13], payload[16..29]);
    }

    [Fact]
    public void The_screen_text_vectors_are_the_scene_line_and_the_alert_title()
    {
        Assert.Equal("#421011AAABBkNBTUFSQQ==", ConsoleProtocol.ScreenTextLine(0, 0, 1, "CAMARA"));
        Assert.Equal("#421011AgICCE1JQyBNVURP", ConsoleProtocol.ScreenTextLine(2, 2, 2, "MIC MUDO"));
    }

    [Fact]
    public void A_text_with_accents_or_enie_goes_out_in_ascii()
    {
        var payload = Payload(ConsoleProtocol.ScreenTextLine(0, 0, 1, "Cámara ñandú ¿sí?"), 0x42);

        Assert.Equal("Camara nandu si?", Encoding.ASCII.GetString(payload[4..]));
        Assert.Equal(16, payload[3]);
    }

    [Fact]
    public void A_text_of_30_letters_is_cut_at_28()
    {
        var line = ConsoleProtocol.ScreenTextLine(1, 0, 1, new string('A', 30));

        var payload = Payload(line, 0x42);
        Assert.Equal(28, payload[3]);
        Assert.Equal(4 + 28, payload.Length);
        Assert.True(line.Length <= ConsoleProtocol.MaxLineLength);
    }

    [Fact]
    public void The_settings_vector_is_the_eight_defaults_with_its_fingerprint_little_endian()
    {
        Assert.Equal("#441011NBIRCAFQAAIPAAM8AAQsAQUAAAboAwcDAAgZAA==", ConsoleProtocol.SettingsLine(Defaults, 0x1234));
    }

    [Fact]
    public void Settings_with_more_than_ten_keys_do_not_fit_the_console()
    {
        var eleven = Enumerable.Range(1, 11).Select(k => ((byte)k, (ushort)0)).ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => ConsoleProtocol.SettingsLine(eleven, 1));
    }

    [Fact]
    public void The_strip_vector_is_three_reds_two_blinking_ambers_and_three_off()
    {
        LedColor[] leds =
        [
            new(0xD2, 0x1F, 0x3C), new(0xD2, 0x1F, 0x3C), new(0xD2, 0x1F, 0x3C),
            new(0xF5, 0xA6, 0x23, LedEffect.Blink1Hz), new(0xF5, 0xA6, 0x23, LedEffect.Blink1Hz),
            LedColor.Off, LedColor.Off, LedColor.Off,
        ];

        Assert.Equal("#461011AAjSHzwA0h88ANIfPAD1piMB9aYjAQAAAAAAAAAAAAAAAA==", ConsoleProtocol.StripLine(leds));
    }

    [Fact]
    public void A_strip_of_more_than_8_leds_does_not_fit_the_console()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ConsoleProtocol.StripLine(Enumerable.Repeat(LedColor.Off, 9).ToArray()));
    }

    [Fact]
    public void Without_a_console_block_the_settings_are_the_defaults_of_02_and_their_fingerprint_is_the_one_of_consola_py()
    {
        var pairs = ConsoleSettingsWire.Pairs(null);

        Assert.Equal(Defaults, pairs);
        // binascii.crc_hqx(pares, 0) de tools/consola.py:65-68, con los valores de fábrica de la 0.2.1 (no_pc con luz y sonido).
        Assert.Equal(0x2F68, ConsoleSettingsWire.Fingerprint(pairs));
        Assert.Equal(ConsoleProtocol.SettingsLine(Defaults, 0x2F68), ConsoleSettingsWire.Line(null));
    }

    [Fact]
    public void The_fingerprint_is_crc16_xmodem_and_zero_becomes_one()
    {
        Assert.Equal(0x31C3, ConsoleSettingsWire.Crc16Xmodem("123456789"u8));
        Assert.Equal(0, ConsoleSettingsWire.Crc16Xmodem([]));
        Assert.Equal(1, ConsoleSettingsWire.Fingerprint([]));
    }

    [Fact]
    public void The_console_block_fills_its_keys_and_the_missing_ones_take_the_default()
    {
        var settings = new ConsoleSettings
        {
            Screen = new ScreenSettings { Brightness = 50, OffAfterSec = 0 },
            HoldMs = 1500,
            SleepAfterSec = 70000,
            Alerts = new Dictionary<string, AlertChannels> { ["no_pc"] = new() { Light = false, Sound = true } },
            Extra = new() { ["strip"] = JsonDocument.Parse("""{ "brightness": 40 }""").RootElement },
        };

        Assert.Equal(
            new (byte, ushort)[] { (1, 50), (2, 15), (3, 60), (4, 0), (5, 65535), (6, 1500), (7, 2), (8, 40) },
            ConsoleSettingsWire.Pairs(settings));
    }

    private static byte[] Payload(string line, byte type)
    {
        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse(line, out var message));
        Assert.Equal(type, message.Type);
        Assert.Equal((byte)0x10, message.Source);
        Assert.Equal((byte)0x11, message.Target);
        return message.Payload;
    }
}
