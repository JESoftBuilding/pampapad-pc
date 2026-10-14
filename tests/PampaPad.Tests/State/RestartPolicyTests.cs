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
}
