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
    [InlineData(ConsoleLinkState.Connected, "Soltar la consola (COM7)")]
    [InlineData(ConsoleLinkState.Waiting, "Soltar la consola (COM7, sin conexión)")]
    [InlineData(ConsoleLinkState.Released, "Tomar la consola (COM7)")]
    public void The_menu_item_says_what_a_click_does_and_how_the_link_is(ConsoleLinkState link, string text)
    {
        var state = new AppState() with { Console = new ConsoleLink(link, "COM7") };

        Assert.Equal(text, TraySummary.ConsoleText(state));
        Assert.Equal(text, TraySummary.From(state).ConsoleItem);
    }
}
