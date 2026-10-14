using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Los bordes del códec de la línea por cable (04 §7) que las líneas de muestra y los cuatro hechos del plan no fijan.</summary>
public class ConsoleProtocolEdgeTests
{
    // Arma la línea con el codificador de la biblioteca, que no es el que se prueba.
    private static string Line(byte type, byte source, byte target, params byte[] payload) =>
        $"#{type:X2}{source:X2}{target:X2}{Convert.ToBase64String(payload)}";

    private static byte[] Bytes(int count) => [.. Enumerable.Range(0, count).Select(i => (byte)((i * 7) + 1))];

    [Fact]
    public void The_constants_are_the_ones_of_the_spec()
    {
        Assert.Equal((byte)0x30, ConsoleProtocol.InputStateType);
        Assert.Equal(12, ConsoleProtocol.KeyCount);
        Assert.Equal(0x0FFF, ConsoleProtocol.KeyMask);
        Assert.Equal(8, ConsoleProtocol.InputStateLength);
        Assert.Equal(1 + 6 + 48, ConsoleProtocol.MaxLineLength);
    }

    [Fact]
    public void The_keys_of_the_cable_are_the_keys_of_a_page()
    {
        Assert.Equal(Validator.KeyCount, ConsoleProtocol.KeyCount);
    }

    [Fact]
    public void Type_source_and_target_come_out_in_their_own_fields()
    {
        var kind = ConsoleProtocol.Parse("#A1B2C3", out var message);

        Assert.Equal(ConsoleLineKind.Message, kind);
        Assert.Equal((byte)0xA1, message.Type);
        Assert.Equal((byte)0xB2, message.Source);
        Assert.Equal((byte)0xC3, message.Target);
    }

    [Fact]
    public void Hexadecimal_digits_can_be_lower_or_upper_case()
    {
        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse("#ab1cDe", out var message));
        Assert.Equal((byte)0xAB, message.Type);
        Assert.Equal((byte)0x1C, message.Source);
        Assert.Equal((byte)0xDE, message.Target);
    }

    [Fact]
    public void Every_value_of_each_header_byte_comes_back_as_it_went_in()
    {
        for (var value = 0; value <= 255; value++)
        {
            var b = (byte)value;

            ConsoleProtocol.Parse(Line(b, 0x11, 0x10), out var type);
            ConsoleProtocol.Parse(Line(0x30, b, 0x10), out var source);
            ConsoleProtocol.Parse(Line(0x30, 0x11, b), out var target);

            Assert.Equal(b, type.Type);
            Assert.Equal(b, source.Source);
            Assert.Equal(b, target.Target);
        }
    }

    [Theory]
    [InlineData('G')]
    [InlineData('g')]
    [InlineData('+')]
    [InlineData('-')]
    [InlineData(' ')]
    [InlineData('x')]
    [InlineData('_')]
    [InlineData('é')]
    [InlineData('３')]
    public void A_character_that_is_not_a_hexadecimal_digit_in_any_of_the_six_places_is_malformed(char bad)
    {
        for (var place = 1; place <= 6; place++)
        {
            var line = "#301110".ToCharArray();
            line[place] = bad;

            var kind = ConsoleProtocol.Parse(new string(line) + "AAAAAAAAAAA=", out _);

            Assert.True(kind == ConsoleLineKind.Malformed, $"El carácter '{bad}' en el lugar {place} dio {kind}.");
        }
    }

    [Fact]
    public void A_line_is_text_unless_it_starts_with_a_hash()
    {
        Assert.Equal(ConsoleLineKind.Text, ConsoleProtocol.Parse(" #301110BAAAAAAAAAA=", out _));
        Assert.Equal(ConsoleLineKind.Text, ConsoleProtocol.Parse("x#301110BAAAAAAAAAA=", out _));
        Assert.Equal(ConsoleLineKind.Text, ConsoleProtocol.Parse("301110BAAAAAAAAAA=", out _));
    }

    [Fact]
    public void A_long_line_that_is_not_a_message_is_text_and_not_malformed()
    {
        Assert.Equal(ConsoleLineKind.Text, ConsoleProtocol.Parse(new string('x', 1000), out _));
    }

    [Fact]
    public void A_line_that_starts_with_a_hash_and_is_not_a_message_is_malformed_even_if_it_is_long()
    {
        Assert.Equal(ConsoleLineKind.Malformed, ConsoleProtocol.Parse("#" + new string('x', 1000), out _));
    }

    [Fact]
    public void The_longest_line_is_55_characters_and_56_is_malformed()
    {
        var longest = Line(0x30, 0x11, 0x10, Bytes(35));
        Assert.Equal(55, longest.Length);
        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse(longest, out var message));
        Assert.Equal(Bytes(35), message.Payload);

        Assert.Equal(ConsoleLineKind.Malformed, ConsoleProtocol.Parse(longest + "A", out _));
    }

    [Fact]
    public void The_limit_counts_every_character_of_the_line_even_one_that_base64_would_skip()
    {
        // La biblioteca ignora los espacios dentro del base64: sin el tope, una línea de 56 con un espacio de más pasaría por buena.
        var longest = Line(0x30, 0x11, 0x10, Bytes(35));

        Assert.Equal(ConsoleLineKind.Malformed, ConsoleProtocol.Parse(longest + " ", out _));
        Assert.Equal(ConsoleLineKind.Malformed, ConsoleProtocol.Parse(longest + "\r", out _));
    }

    [Fact]
    public void The_buffer_fits_the_longest_line_even_without_padding()
    {
        // 48 caracteres de base64 sin relleno son 36 bytes, uno más que la carga máxima de 04 §7: el sobrante no es motivo para tirar la línea.
        var line = "#301110" + new string('A', 48);
        Assert.Equal(ConsoleProtocol.MaxLineLength, line.Length);

        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse(line, out var message));
        Assert.Equal(36, message.Payload.Length);
    }

    [Fact]
    public void Payloads_from_0_to_36_bytes_make_a_message_and_37_makes_a_line_too_long()
    {
        for (var count = 0; count <= 36; count++)
        {
            var kind = ConsoleProtocol.Parse(Line(0x30, 0x11, 0x10, Bytes(count)), out var message);

            Assert.True(kind == ConsoleLineKind.Message, $"{count} bytes dieron {kind}.");
            Assert.Equal(Bytes(count), message.Payload);
        }

        Assert.Equal(ConsoleLineKind.Malformed, ConsoleProtocol.Parse(Line(0x30, 0x11, 0x10, Bytes(37)), out _));
    }

    [Fact]
    public void The_payload_is_the_exact_length_of_what_was_decoded()
    {
        ConsoleProtocol.Parse("#301110BAAAAAAAAAA=", out var eight);
        ConsoleProtocol.Parse("#301110AQID", out var three);

        Assert.Equal(8, eight.Payload.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, three.Payload);
    }

    [Fact]
    public void The_plus_and_slash_of_standard_base64_decode_and_the_url_variant_does_not()
    {
        var payload = new byte[] { 0xFB, 0xEF, 0xBF, 0, 0, 0, 0, 0 };
        Assert.Equal("+++/AAAAAAA=", Convert.ToBase64String(payload));

        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse("#301110+++/AAAAAAA=", out var message));
        Assert.Equal(payload, message.Payload);
        Assert.Equal(ConsoleLineKind.Malformed, ConsoleProtocol.Parse("#301110---_AAAAAAA=", out _));
    }

    [Theory]
    [InlineData("#301110=")]
    [InlineData("#301110A")]
    [InlineData("#301110AA")]
    [InlineData("#301110AAA")]
    [InlineData("#301110AAAAA")]
    [InlineData("#301110AA=A")]
    [InlineData("#301110====")]
    public void Base64_that_does_not_close_is_malformed(string line)
    {
        Assert.Equal(ConsoleLineKind.Malformed, ConsoleProtocol.Parse(line, out _));
    }

    [Fact]
    public void A_line_that_is_not_a_message_leaves_an_empty_payload_and_not_a_null()
    {
        ConsoleProtocol.Parse("arranque", out var text);
        ConsoleProtocol.Parse("#3G1110AAAA", out var malformed);

        Assert.NotNull(text.Payload);
        Assert.Empty(text.Payload);
        Assert.NotNull(malformed.Payload);
        Assert.Empty(malformed.Payload);
    }

    [Fact]
    public void Eight_bytes_make_an_input_state_and_seven_do_not()
    {
        Assert.True(ConsoleProtocol.TryDecodeInputState(new byte[8], out var eight));
        Assert.Equal(0, eight.Keys);

        Assert.False(ConsoleProtocol.TryDecodeInputState(new byte[7], out var seven));
        Assert.Equal(default, seven);
        Assert.False(ConsoleProtocol.TryDecodeInputState([], out _));
    }

    [Fact]
    public void Anything_after_the_eighth_byte_is_not_looked_at()
    {
        var payload = new byte[ConsoleProtocol.InputStateLength + 28];
        payload.AsSpan(ConsoleProtocol.InputStateLength).Fill(0xFF);
        payload[0] = 0x05;

        Assert.True(ConsoleProtocol.TryDecodeInputState(payload, out var state));
        Assert.Equal(0b0101, state.Keys);
    }

    [Fact]
    public void Only_the_first_two_bytes_say_which_keys_are_down()
    {
        var payload = new byte[] { 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

        Assert.True(ConsoleProtocol.TryDecodeInputState(payload, out var state));
        Assert.Equal(0, state.Keys);
        Assert.Equal(0, state.Buttons);
    }

    [Fact]
    public void The_buttons_are_the_first_byte_low_and_the_second_high_and_keys_are_the_first_twelve_bits()
    {
        Assert.True(ConsoleProtocol.TryDecodeInputState(new byte[] { 0x34, 0x12, 0, 0, 0, 0, 0, 0 }, out var state));
        Assert.Equal(0x1234, state.Buttons);
        Assert.Equal(0x234, state.Keys);

        // Los bits 12 a 15 (perilla, tecla del módulo y reservados) no son teclas.
        Assert.True(ConsoleProtocol.TryDecodeInputState(new byte[] { 0x04, 0xF0, 0, 0, 0, 0, 0, 0 }, out var high));
        Assert.Equal(0xF004, high.Buttons);
        Assert.Equal(0b0100, high.Keys);
    }

    [Fact]
    public void Each_of_the_twelve_bits_is_one_key_and_the_first_bit_is_key_1()
    {
        for (var bit = 0; bit < ConsoleProtocol.KeyCount; bit++)
        {
            var buttons = 1 << bit;
            var payload = new byte[] { (byte)(buttons & 0xFF), (byte)(buttons >> 8), 0, 0, 0, 0, 0, 0 };

            Assert.True(ConsoleProtocol.TryDecodeInputState(payload, out var state));
            Assert.Equal(buttons, state.Keys);
        }
    }

    [Fact]
    public void Every_button_pattern_of_two_bytes_survives_the_cable()
    {
        for (var buttons = 0; buttons <= ushort.MaxValue; buttons++)
        {
            var line = Line(0x30, 0x11, 0x10, (byte)(buttons & 0xFF), (byte)(buttons >> 8), 0, 0, 0, 0, 0, 0);

            Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse(line, out var message));
            Assert.True(ConsoleProtocol.TryDecodeInputState(message.Payload, out var state));
            Assert.Equal(buttons, state.Buttons);
            Assert.Equal(buttons & 0x0FFF, state.Keys);
        }
    }

    [Fact]
    public void No_keys_down_is_the_default_state()
    {
        Assert.Equal(0, default(InputState).Keys);
        Assert.Equal(new InputState(0), default);
    }
}
