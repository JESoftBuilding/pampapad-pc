using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La falla con hora que lee la consola para sus avisos (04:147; plan de la 9a, E9-5; plan de la 9b, E9b-2).</summary>
public class NotifierRecentFailureTests
{
    [Fact]
    public async Task A_failed_action_leaves_its_name_step_code_and_time_and_dismissing_the_icon_does_not_erase_it()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 20, 0, 0, TimeSpan.Zero));
        await using var rig = new NotifierRig(clock: clock);

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 2, "wait.window", ErrorCodes.Cancelled, "Se canceló."));
        rig.Notifier.Dismiss();

        Assert.Equal(new RecentFailure(Notifier.ActionFailed, "Marca: short", 2, clock.GetUtcNow(), ErrorCodes.Cancelled), rig.State.Current.RecentFailure);
    }

    [Fact]
    public async Task A_test_from_the_window_that_is_not_a_marker_leaves_nothing_for_the_console()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, "wait.window", ErrorCodes.Cancelled, "No.", Trigger.Window));

        Assert.Null(rig.State.Current.RecentFailure);
    }

    // marker_failed sale en la consola por cualquier vía (plan de la 9b, decisión 7); el cartel, el sonido y el ícono de la ventana y del MCP no cambian.
    [Theory]
    [InlineData(Trigger.Window)]
    [InlineData(Trigger.Mcp)]
    public async Task A_marker_that_failed_from_the_window_or_the_mcp_reaches_the_console_and_nothing_else(Trigger by)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 20, 0, 0, TimeSpan.Zero));
        await using var rig = new NotifierRig(clock: clock);

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, "obs.marker", ErrorCodes.MarkerPaused, "Estás en pausa.", by));

        Assert.Equal(new RecentFailure(Notifier.MarkerFailed, "Marca: short", 1, clock.GetUtcNow(), ErrorCodes.MarkerPaused), rig.State.Current.RecentFailure);
        Assert.Null(rig.State.Current.LastFailure);
        Assert.Empty(rig.Sounds);
        Assert.Empty(rig.Balloons);
    }
}
