using PampaPad.Core;

namespace PampaPad.Tests;

public class ObsStateTests
{
    [Fact]
    public void Starts_without_obs_and_not_recording()
    {
        var obs = new AppState().Obs;

        Assert.Equal(ObsLinkState.Closed, obs.Link);
        Assert.Equal(RecordingState.Stopped, obs.Recording);
        Assert.False(obs.Streaming);
        Assert.False(obs.TakeInProgress);
    }

    [Theory]
    [InlineData(RecordingState.Starting)]
    [InlineData(RecordingState.Recording)]
    [InlineData(RecordingState.Paused)]
    [InlineData(RecordingState.Stopping)]
    public void A_take_is_in_progress_in_every_state_but_stopped(RecordingState recording)
    {
        var obs = ObsState.Initial with { Recording = recording };

        Assert.True(obs.TakeInProgress);
    }

    [Fact]
    public void Closed_means_neither_recording_nor_streaming()
    {
        var recording = ObsState.Initial with
        {
            Link = ObsLinkState.Ready,
            Recording = RecordingState.Paused,
            Streaming = true,
            Recorded = TimeSpan.FromSeconds(9),
        };

        var closed = recording.WithLink(ObsLinkState.Closed);

        Assert.Equal(ObsLinkState.Closed, closed.Link);
        Assert.Equal(RecordingState.Stopped, closed.Recording);
        Assert.False(closed.Streaming);
        Assert.Equal(TimeSpan.Zero, closed.Recorded);
    }

    [Theory]
    [InlineData(ObsLinkState.Lost)]
    [InlineData(ObsLinkState.NoWebsocket)]
    [InlineData(ObsLinkState.Exiting)]
    [InlineData(ObsLinkState.Kicked)]
    public void Any_other_cut_keeps_the_last_known_take(ObsLinkState link)
    {
        var recording = ObsState.Initial with { Link = ObsLinkState.Ready, Recording = RecordingState.Recording, Streaming = true };

        var cut = recording.WithLink(link);

        Assert.Equal(link, cut.Link);
        Assert.Equal(RecordingState.Recording, cut.Recording);
        Assert.True(cut.Streaming);
    }
}
