using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>La falla con hora que lee la consola para su aviso leve (04:147; plan de la 9a, E9-5).</summary>
public class NotifierRecentFailureTests
{
    [Fact]
    public async Task A_failed_action_leaves_its_name_step_and_time_and_dismissing_the_icon_does_not_erase_it()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 20, 0, 0, TimeSpan.Zero));
        await using var rig = new NotifierRig(clock: clock);

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 2, "wait.window", ErrorCodes.Cancelled, "Se canceló."));
        rig.Notifier.Dismiss();

        Assert.Equal(new RecentFailure(Notifier.ActionFailed, "Marca: short", 2, clock.GetUtcNow()), rig.State.Current.RecentFailure);
    }

    [Fact]
    public async Task A_test_from_the_window_leaves_nothing_for_the_console()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, "obs.marker", ErrorCodes.MarkerNotRecording, "No.", Trigger.Window));

        Assert.Null(rig.State.Current.RecentFailure);
    }
}
