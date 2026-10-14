using PampaPad.Core;

namespace PampaPad.Tests;

public class RestartPolicyTests
{
    [Theory]
    [InlineData(UpdatePhase.Idle)]
    [InlineData(UpdatePhase.Available)]
    public void Nothing_in_the_stage_1_state_forbids_a_restart(UpdatePhase phase)
    {
        var state = AppState.Initial with { Update = new UpdateState(phase, "0.1.1") };

        var verdict = RestartPolicy.Evaluate(state);

        Assert.True(verdict.Allowed);
        Assert.Null(verdict.Reason);
    }

    private static AppState With(ObsState obs) => AppState.Initial with { Obs = obs };

    private static ObsState Connected(RecordingState recording, bool streaming = false) =>
        ObsState.Initial with { Link = ObsLinkState.Ready, Recording = recording, Streaming = streaming };

    [Fact]
    public void Stopped_and_not_streaming_it_can_restart()
    {
        Assert.True(RestartPolicy.Evaluate(With(Connected(RecordingState.Stopped))).Allowed);
    }

    [Theory]
    [InlineData(RecordingState.Starting)]
    [InlineData(RecordingState.Recording)]
    [InlineData(RecordingState.Paused)]
    [InlineData(RecordingState.Stopping)]
    public void A_take_in_progress_blocks_the_restart(RecordingState recording)
    {
        Assert.False(RestartPolicy.Evaluate(With(Connected(recording))).Allowed);
        Assert.NotNull(RestartPolicy.Evaluate(With(Connected(recording))).Reason);
    }

    [Fact]
    public void Streaming_blocks_the_restart()
    {
        Assert.False(RestartPolicy.Evaluate(With(Connected(RecordingState.Stopped, streaming: true))).Allowed);
    }

    [Fact]
    public void A_cut_link_keeps_blocking_with_the_last_known_take()
    {
        var lost = Connected(RecordingState.Recording).WithLink(ObsLinkState.Lost);

        Assert.False(RestartPolicy.Evaluate(With(lost)).Allowed);
    }

    [Fact]
    public void A_closed_obs_does_not_block()
    {
        var closed = Connected(RecordingState.Recording, streaming: true).WithLink(ObsLinkState.Closed);

        Assert.True(RestartPolicy.Evaluate(With(closed)).Allowed);
    }
}
