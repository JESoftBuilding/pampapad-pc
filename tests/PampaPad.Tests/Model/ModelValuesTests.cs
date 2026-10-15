using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ModelValuesTests
{
    private const HotkeyModifiers All = HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Alt;

    [Theory]
    [InlineData("Ctrl+Shift+Alt+F7", All, 0x76)]
    [InlineData("ctrl+alt+shift+f5", All, 0x74)]
    [InlineData(" Ctrl + Shift + Alt + F9 ", All, 0x78)]
    [InlineData("Alt+A", HotkeyModifiers.Alt, 0x41)]
    [InlineData("Shift+9", HotkeyModifiers.Shift, 0x39)]
    [InlineData("Ctrl+F1", HotkeyModifiers.Ctrl, 0x70)]
    [InlineData("Ctrl+F24", HotkeyModifiers.Ctrl, 0x87)]
    public void Parses_a_hotkey(string text, HotkeyModifiers modifiers, int virtualKey)
    {
        Assert.Equal(new Hotkey(modifiers, virtualKey), Hotkey.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("F7")]
    [InlineData("Win+F7")]
    [InlineData("Ctrl+Ctrl+F7")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F01")]
    [InlineData("Ctrl+AB")]
    [InlineData("Ctrl+Ñ")]
    [InlineData("Ctrl+Space")]
    public void Rejects_what_is_not_a_hotkey(string? text)
    {
        Assert.Null(Hotkey.Parse(text));
    }

    [Fact]
    public void Writes_a_hotkey_in_the_fixed_order()
    {
        Assert.Equal("Ctrl+Shift+Alt+F5", Hotkey.Parse("alt+SHIFT+ctrl+f5")!.Value.ToString());
        Assert.Equal("Alt+A", Hotkey.Parse("alt+a")!.Value.ToString());
    }

    [Fact]
    public void Every_key_of_the_format_is_written_and_read_back_the_same()
    {
        // F1 a F24, A a Z y 0 a 9: la ida y vuelta fija también el borde F24 de ToString (0x87), que Parse no usa.
        var keys = Enumerable.Range(1, 24).Select(number => "F" + number)
            .Concat(Enumerable.Range('A', 26).Select(letter => ((char)letter).ToString()))
            .Concat(Enumerable.Range(0, 10).Select(digit => digit.ToString()));

        foreach (string key in keys)
        {
            string text = "Ctrl+Shift+Alt+" + key;
            Hotkey hotkey = Hotkey.Parse(text)!.Value;

            Assert.Equal(text, hotkey.ToString());
            Assert.Equal(hotkey, Hotkey.Parse(hotkey.ToString()));
        }
    }

    [Fact]
    public void The_marker_types_are_the_five_of_the_contract_and_are_not_translated()
    {
        Assert.Equal(new[] { "short", "inicio", "fin", "sacar", "largo" }, MarkerTypes.All);
        Assert.True(MarkerTypes.TryParse("sacar", out MarkerType type));
        Assert.Equal(MarkerType.Sacar, type);
        Assert.Equal("sacar", MarkerTypes.Text(type));
        Assert.False(MarkerTypes.TryParse("Short", out _));
        Assert.False(MarkerTypes.TryParse("corte", out _));
        Assert.False(MarkerTypes.TryParse(null, out _));
    }

    [Fact]
    public void An_obs_reference_accepts_the_name_alone()
    {
        Assert.Equal(new ObsRef(null, "Banco"), JsonSerializer.Deserialize<ObsRef>("\"Banco\"", Json.Options));
        Assert.Equal(
            new ObsRef("900ebc25", "Banco"),
            JsonSerializer.Deserialize<ObsRef>("""{ "uuid": "900ebc25", "name": "Banco", "otro": [1, 2] }""", Json.Options));
    }

    [Fact]
    public void An_obs_reference_without_uuid_writes_only_the_name()
    {
        string written = JsonSerializer.Serialize(new ObsRef(null, "Banco"), Json.Options);

        Assert.DoesNotContain("uuid", written);
        Assert.Contains("\"name\": \"Banco\"", written);
    }
}
