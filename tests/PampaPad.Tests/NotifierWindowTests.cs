using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class NotifierWindowTests
{
    [Fact]
    public async Task A_test_from_the_window_does_not_sound_show_a_balloon_touch_the_icon_or_clear_the_last_failure()
    {
        await using var rig = new NotifierRig();
        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerNotRecording, "No estás grabando: la marca no entró."));
        ActionFailure? before = rig.State.Current.LastFailure;
        rig.Sounds.Clear();
        rig.Balloons.Clear();

        rig.Notifier.OnFinished(NotifierRig.Failed("prueba", 1, ObsMarker.TypeId, ErrorCodes.MarkerPaused, "Estás en pausa.", Trigger.Window));
        rig.Notifier.OnFinished(NotifierRig.Succeeded("prueba") with { RequestedBy = Trigger.Window });

        Assert.NotNull(before);
        Assert.Equal(before, rig.State.Current.LastFailure);
        Assert.Empty(rig.Sounds);
        Assert.Empty(rig.Balloons);
        Assert.DoesNotContain(rig.Log.Recent(), entry => entry.Module == "notifier" && entry.Message.Contains("prueba", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_same_failure_from_a_key_still_sounds_and_shows_its_balloon()
    {
        await using var rig = new NotifierRig();

        rig.Notifier.OnFinished(NotifierRig.Failed("marca-short", 1, ObsMarker.TypeId, ErrorCodes.MarkerNotRecording, "No estás grabando: la marca no entró.", Trigger.Key));

        Assert.Single(rig.Sounds);
        Assert.Single(rig.Balloons);
    }
}
