using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Lo que la etapa 5 suma a la bandeja (05 §3): "Chequeo previo", "Cancelar todo" y la página base en el texto de ayuda.</summary>
public class TraySummaryChainsTests
{
    private static readonly AppState Connected = AppState.Initial with
    {
        Obs = ObsState.Initial with { Link = ObsLinkState.Ready },
        BasePage = "grabacion",
        MainPage = "grabacion",
    };

    [Theory]
    [InlineData(ObsLinkState.Ready, true)]
    [InlineData(ObsLinkState.Loading, false)]
    [InlineData(ObsLinkState.Closed, false)]
    [InlineData(ObsLinkState.Lost, false)]
    public void The_preflight_item_is_there_with_obs_connected(ObsLinkState link, bool shown)
    {
        TraySummary summary = TraySummary.From(Connected with { Obs = Connected.Obs with { Link = link } });

        Assert.Equal(shown, summary.CanRunPreflight);
    }

    [Fact]
    public void Cancel_all_is_there_while_some_action_runs()
    {
        TraySummary idle = TraySummary.From(Connected);
        TraySummary busy = TraySummary.From(Connected with { Running = [new RunningAction(Guid.NewGuid(), "ir-al-banco", Trigger.Hotkey)] });

        Assert.False(idle.CanCancel);
        Assert.True(busy.CanCancel);
    }

    [Fact]
    public void The_tooltip_names_the_base_page_when_it_is_not_the_main_one()
    {
        TraySummary main = TraySummary.From(Connected);
        TraySummary scenes = TraySummary.From(Connected with { BasePage = "escenas" });

        Assert.Equal("PampaPad · OBS conectado", main.Tooltip);
        Assert.Equal("PampaPad · OBS conectado · página escenas", scenes.Tooltip);
    }

    [Fact]
    public void Without_pages_nothing_is_named()
    {
        Assert.Equal("PampaPad · OBS conectado", TraySummary.From(Connected with { BasePage = "", MainPage = "" }).Tooltip);
    }
}
