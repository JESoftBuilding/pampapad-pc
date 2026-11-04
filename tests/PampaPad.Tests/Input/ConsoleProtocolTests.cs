using PampaPad.Core;

namespace PampaPad.Tests;

public class ConsoleProtocolTests
{
    [Fact]
    public void The_example_of_the_spec_is_key_3_pressed()
    {
        var kind = ConsoleProtocol.Parse("#301110BAAAAAAAAAA=", out var message);

        Assert.Equal(ConsoleLineKind.Message, kind);
        Assert.Equal((byte)0x30, message.Type);
        Assert.Equal((byte)0x11, message.Source);
        Assert.Equal((byte)0x10, message.Target);
        Assert.Equal(new byte[] { 4, 0, 0, 0, 0, 0, 0, 0 }, message.Payload);
        Assert.True(ConsoleProtocol.TryDecodeInputState(message.Payload, out var state));
        Assert.Equal(0b0100, state.Keys);
    }

    [Fact]
    public void An_empty_line_is_text()
    {
        Assert.Equal(ConsoleLineKind.Text, ConsoleProtocol.Parse("", out _));
    }

    [Fact]
    public void A_line_longer_than_64_characters_is_malformed()
    {
        var line = "#301110" + new string('A', 58);

        Assert.Equal(ConsoleLineKind.Malformed, ConsoleProtocol.Parse(line, out _));
    }

    [Fact]
    public void A_header_without_payload_is_a_message_with_no_bytes()
    {
        var kind = ConsoleProtocol.Parse("#301110", out var message);

        Assert.Equal(ConsoleLineKind.Message, kind);
        Assert.Empty(message.Payload);
        Assert.False(ConsoleProtocol.TryDecodeInputState(message.Payload, out _));
    }

    [Fact]
    public void Power_toggle_goes_down_the_cable_as_the_line_of_the_spec()
    {
        Assert.Equal("#101013BQAAAAI=", ConsoleProtocol.LightLine(new LightCommand(5, 0, 0, 0, 2)));
    }

    [Theory]
    [InlineData(1, 0x6D, 0x5D, 0xF6, 0, "#101013AW1d9gA=")]
    [InlineData(2, 0, 0, 0, 128, "#101013AgAAAIA=")]
    [InlineData(3, 0, 0, 0, 0xF6, "#101013AwAAAPY=")]
    [InlineData(4, 0, 0, 0, 2, "#101013BAAAAAI=")]
    public void Each_light_command_is_type_0x10_from_the_pc_to_the_lights_with_its_five_bytes(byte cmd, byte r, byte g, byte b, byte value, string expected)
    {
        // Las líneas salen a mano de 04 §7: tipo 0x10, origen 16, destino 19 y la carga en base64. Se leen de vuelta con el mismo códec.
        string line = ConsoleProtocol.LightLine(new LightCommand(cmd, r, g, b, value));

        Assert.Equal(expected, line);
        Assert.True(line.Length <= ConsoleProtocol.MaxLineLength);
        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse(line, out var message));
        Assert.Equal(new ConsoleMessage(ConsoleProtocol.LightCommandType, 0x10, 0x13, []) with { Payload = message.Payload }, message);
        Assert.Equal(new[] { cmd, r, g, b, value }, message.Payload);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Sample_lines_fall_in_their_class(string expected, string keys, string line)
    {
        var kind = ConsoleProtocol.Parse(line, out var message);
        var decoded = ConsoleProtocol.TryDecodeInputState(message.Payload, out var state);

        var actual = kind switch
        {
            ConsoleLineKind.Text => "text",
            ConsoleLineKind.Malformed => "malformed",
            _ when message.Type != ConsoleProtocol.InputStateType => "unknown",
            _ when !decoded => "short",
            _ => "keys",
        };

        Assert.Equal(expected, actual);
        if (actual == "keys")
        {
            Assert.Equal(Convert.ToInt32(keys, 16), state.Keys);
        }
    }

    public static TheoryData<string, string, string> Samples()
    {
        var data = new TheoryData<string, string, string>();
        var file = Path.Combine(AppContext.BaseDirectory, "Data", "console-lines.txt");
        foreach (var row in File.ReadLines(file))
        {
            if (row.Length == 0 || row[0] == ';')
            {
                continue;
            }

            var parts = row.Split(' ', 3);
            data.Add(parts[0], parts[1], parts[2]);
        }

        return data;
    }
}
