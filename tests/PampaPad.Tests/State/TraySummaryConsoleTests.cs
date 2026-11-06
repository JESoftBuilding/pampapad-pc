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

    [Fact]
    public void With_the_receiver_and_the_console_without_signal_it_says_so()
    {
        var link = new ConsoleLink(ConsoleLinkState.Connected, "COM21") { Receiver = new ReceiverLink("0.1.0", false) };

        Assert.Equal("Soltar la consola (COM21 · Receptor conectado · consola sin señal)", TraySummary.ConsoleText(new AppState() with { Console = link }));
    }

    [Fact]
    public void With_the_receiver_hearing_the_console_it_says_by_radio_and_the_hello()
    {
        var link = new ConsoleLink(ConsoleLinkState.Connected, "COM21") { Receiver = new ReceiverLink("0.1.0", true), Firmware = "0.3.0", SettingsInSync = true };

        Assert.Equal("Soltar la consola (COM21 · Consola conectada por radio · firmware 0.3.0 · ajustes al día)", TraySummary.ConsoleText(new AppState() with { Console = link }));
    }

    [Theory]
    [InlineData(ConsoleLinkState.Waiting, null, "Soltar la consola (Automático, sin conexión)")]
    [InlineData(ConsoleLinkState.Waiting, "COM21", "Soltar la consola (Automático · COM21, sin conexión)")]
    [InlineData(ConsoleLinkState.Connected, "COM21", "Soltar la consola (Automático · COM21 · sin saludo todavía)")]
    [InlineData(ConsoleLinkState.Released, "COM21", "Tomar la consola (Automático · COM21)")]
    public void With_automatic_the_port_is_named_automatic_and_the_one_in_use(ConsoleLinkState state, string? port, string text)
    {
        var link = new ConsoleLink(state, port) { Auto = true };

        Assert.Equal(text, TraySummary.ConsoleText(new AppState() with { Console = link }));
    }
}
