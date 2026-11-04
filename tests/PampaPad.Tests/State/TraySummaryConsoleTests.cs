using PampaPad.Core;

namespace PampaPad.Tests;

public class TraySummaryConsoleTests
{
    [Fact]
    public void Without_a_port_there_is_no_console_and_no_menu_item()
    {
        Assert.Equal(ConsoleLinkState.Off, new AppState().Console.State);
        Assert.Null(new AppState().Console.Port);
        Assert.Null(TraySummary.ConsoleText(new AppState()));
        Assert.Null(TraySummary.From(new AppState()).ConsoleItem);
    }

    [Theory]
    [InlineData(ConsoleLinkState.Connected, "Soltar la consola (COM7 · sin saludo todavía)")]
    [InlineData(ConsoleLinkState.Waiting, "Soltar la consola (COM7, sin conexión)")]
    [InlineData(ConsoleLinkState.Released, "Tomar la consola (COM7)")]
    public void The_menu_item_says_what_a_click_does_and_how_the_link_is(ConsoleLinkState link, string text)
    {
        var state = new AppState() with { Console = new ConsoleLink(link, "COM7") };

        Assert.Equal(text, TraySummary.ConsoleText(state));
        Assert.Equal(text, TraySummary.From(state).ConsoleItem);
    }

    [Theory]
    [InlineData(true, "Soltar la consola (COM7 · firmware 0.1.5 · ajustes al día)")]
    [InlineData(false, "Soltar la consola (COM7 · firmware 0.1.5 · ajustes sin confirmar)")]
    [InlineData(null, "Soltar la consola (COM7 · firmware 0.1.5 · ajustes sin confirmar)")]
    public void Connected_it_says_the_firmware_of_the_hello_and_whether_the_settings_are_confirmed(bool? inSync, string text)
    {
        var state = new AppState() with { Console = new ConsoleLink(ConsoleLinkState.Connected, "COM7") { Firmware = "0.1.5", SettingsInSync = inSync } };

        Assert.Equal(text, TraySummary.ConsoleText(state));
    }
}
