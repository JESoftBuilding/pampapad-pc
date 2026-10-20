using PampaPad.Core;

namespace PampaPad.Tests;

public class DispatcherCancelCountTests
{
    [Fact]
    public async Task CancelAll_says_how_many_it_cancelled()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);
        System.Action release = rig.Hold();
        Task<ActionResult> first = rig.Dispatcher.Launch("marca-short", Invocation());
        Task<ActionResult> second = rig.Dispatcher.Launch("marca-inicio", Invocation());
        await Poll.Until(() => rig.State.Current.Running.Count == 2);

        int cancelled = rig.Dispatcher.CancelAll();
        release();
        await Task.WhenAll(first, second);

        Assert.Equal(2, cancelled);
    }

    [Fact]
    public async Task CancelAll_with_nothing_running_says_zero()
    {
        await using var rig = new DispatcherRig(DispatcherRig.Example);

        Assert.Equal(0, rig.Dispatcher.CancelAll());
    }

    private static Invocation Invocation() => new(Guid.NewGuid(), Trigger.Tray, null, null, null, null);
}
