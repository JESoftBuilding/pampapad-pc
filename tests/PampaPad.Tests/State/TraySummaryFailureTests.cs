using PampaPad.Core;

namespace PampaPad.Tests;

public class TraySummaryFailureTests
{
    private static readonly ActionFailure MarkerInPause = new("marker_failed", "Falló Marca: fin: paso 1 (obs.marker)", "Estás en pausa: la marca no entró.");

    private static AppState Paused()
    {
        var empty = new AppState();
        return empty with { Obs = empty.Obs with { Link = ObsLinkState.Ready, Recording = RecordingState.Paused } };
    }

    [Fact]
    public void A_failed_action_wins_the_icon_and_the_tooltip_names_everything()
    {
        TraySummary summary = TraySummary.From(Paused() with { LastFailure = MarkerInPause });

        Assert.Equal(TrayIcon.ActionFailed, summary.Icon);
        Assert.StartsWith("Falló Marca: fin: paso 1 (obs.marker). Estás en pausa: la marca no entró.", summary.Tooltip);
        Assert.Contains("En pausa", summary.Tooltip);
    }

    [Fact]
    public void Without_a_failure_nothing_changes()
    {
        Assert.Null(new AppState().LastFailure);
        Assert.Equal(TrayIcon.Paused, TraySummary.From(Paused()).Icon);
    }
}
