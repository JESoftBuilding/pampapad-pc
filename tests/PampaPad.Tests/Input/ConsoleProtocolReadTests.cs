using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que la consola 0.1.5 manda además de las teclas (plan de la 9a, E9-2): la perilla, PUSH, la tecla del módulo y el Hello.</summary>
public class ConsoleProtocolReadTests
{
    [Fact]
    public void The_input_state_vector_of_the_firmware_is_key_1_and_the_module_key_with_the_knob_at_3()
    {
        // test_consolewire/test_main.cpp:38.
        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse("#301110ASAAAAAAAAM=", out var message));
        Assert.True(ConsoleProtocol.TryDecodeInputState(message.Payload, out var state));

        Assert.Equal(0b1, state.Keys);
        Assert.True(state.ModuleKey);
        Assert.False(state.Push);
        Assert.Equal(3, state.Knob);
    }

    [Fact]
    public void Key_12_is_the_last_key_bit_and_not_the_push()
    {
        ConsoleProtocol.Parse("#301110AAgAAAAAAAA=", out var message);
        Assert.True(ConsoleProtocol.TryDecodeInputState(message.Payload, out var state));

        Assert.Equal(0x0800, state.Keys);
        Assert.False(state.Push);
        Assert.False(state.ModuleKey);
    }

    [Fact]
    public void Bit_12_is_the_push_and_bit_13_the_module_key_and_neither_is_a_key()
    {
        Assert.True(ConsoleProtocol.TryDecodeInputState(new byte[] { 0, 0x10, 0, 0, 0, 0, 0, 0 }, out var push));
        Assert.True(ConsoleProtocol.TryDecodeInputState(new byte[] { 0, 0x20, 0, 0, 0, 0, 0, 0 }, out var module));

        Assert.True(push.Push);
        Assert.False(push.ModuleKey);
        Assert.Equal(0, push.Keys);
        Assert.True(module.ModuleKey);
        Assert.False(module.Push);
        Assert.Equal(0, module.Keys);
    }

    [Fact]
    public void The_knob_is_byte_7_and_bytes_2_to_6_are_not_looked_at()
    {
        Assert.True(ConsoleProtocol.TryDecodeInputState(new byte[] { 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 250 }, out var state));

        Assert.Equal(250, state.Knob);
        Assert.Equal(0, state.Buttons);
    }

    [Fact]
    public void The_hello_vector_of_the_firmware_is_level_1_version_0_1_0_just_booted_and_no_fingerprint()
    {
        // test_consolewire/test_main.cpp:32 y 04:601.
        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse("#311110AQABAMOAAAA=", out var message));
        Assert.Equal(ConsoleProtocol.HelloType, message.Type);
        Assert.True(ConsoleProtocol.TryDecodeHello(message.Payload, out var hello));

        Assert.Equal(new Hello(1, "0.1.0", 0xC3, 0, true, 0), hello);
    }

    [Fact]
    public void The_energy_is_bits_0_and_1_of_byte_5_and_the_fingerprint_is_little_endian()
    {
        Assert.True(ConsoleProtocol.TryDecodeHello(new byte[] { 1, 0, 1, 5, 0xC7, 0x7E, 0xEB, 0x6B }, out var hello));

        Assert.Equal(2, hello.Energy);
        Assert.False(hello.JustBooted);
        Assert.Equal("0.1.5", hello.Version);
        Assert.Equal(0xC7, hello.Capabilities);
        Assert.Equal(0x6BEB, hello.SettingsFingerprint);
    }

    [Fact]
    public void A_hello_shorter_than_8_bytes_does_not_decode_and_a_longer_one_ignores_the_tail()
    {
        Assert.False(ConsoleProtocol.TryDecodeHello(new byte[7], out _));
        Assert.True(ConsoleProtocol.TryDecodeHello(new byte[] { 1, 0, 1, 5, 0xC7, 0, 0, 0, 0xFF, 0xFF, 0xFF }, out var hello));
        Assert.Equal("0.1.5", hello.Version);
    }

    [Fact]
    public void An_unknown_type_of_the_firmware_tests_is_a_message_that_is_not_an_input_state_nor_a_hello()
    {
        // test_consolemodel/test_main.cpp:216.
        Assert.Equal(ConsoleLineKind.Message, ConsoleProtocol.Parse("#3F1011AAAA", out var message));

        Assert.Equal((byte)0x3F, message.Type);
        Assert.NotEqual(ConsoleProtocol.InputStateType, message.Type);
        Assert.NotEqual(ConsoleProtocol.HelloType, message.Type);
    }
}
