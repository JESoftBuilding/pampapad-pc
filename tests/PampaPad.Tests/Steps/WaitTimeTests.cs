using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;
using PadAction = PampaPad.Core.Action;

namespace PampaPad.Tests;

public class WaitTimeTests
{
    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.3", Gesture.Tap, 0);

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    private static PadAction Chain(string steps) =>
        JsonSerializer.Deserialize<PadAction>($$"""{ "id": "espera", "name": "Espera", "steps": {{steps}} }""", Json.Options)!;

    [Fact]
    public async Task It_waits_exactly_the_time_of_the_clock()
    {
        var clock = new FakeTimeProvider();
        var wait = new WaitTime(clock);

        Task<StepResult> running = wait.RunAsync(Parse("""{ "type": "wait.time", "durationMs": 500 }"""), Tap, CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(499));
        bool early = running.IsCompleted;
        clock.Advance(TimeSpan.FromMilliseconds(1));
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(early);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task Cancelling_the_chain_while_it_waits_is_cancelled_and_the_next_step_does_not_run()
    {
        var clock = new FakeTimeProvider();
        var after = new FakeStep("a");
        var executor = new Executor([new WaitTime(clock), after], clock);
        using var cancel = new CancellationTokenSource();

        Task<ActionResult> running = executor.RunAsync(Chain("""[ { "type": "wait.time", "durationMs": 10000 }, { "type": "a" } ]"""), Tap, cancel.Token);
        await cancel.CancelAsync();
        ActionResult result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal(1, result.StepNumber);
        Assert.Empty(after.Runs);
    }

    [Theory]
    [InlineData("""{ "type": "wait.time" }""")]
    [InlineData("""{ "type": "wait.time", "durationMs": 0 }""")]
    [InlineData("""{ "type": "wait.time", "durationMs": -5 }""")]
    [InlineData("""{ "type": "wait.time", "durationMs": 60001 }""")]
    [InlineData("""{ "type": "wait.time", "durationMs": "500" }""")]
    [InlineData("""{ "type": "wait.time", "durationMs": 1.5 }""")]
    public void A_duration_that_is_not_from_1_ms_to_the_action_limit_is_a_problem(string json)
    {
        Problem problem = Assert.Single(new WaitTime(TimeProvider.System).Validate(Parse(json)));

        Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
    }

    [Fact]
    public void The_limit_of_the_action_is_the_longest_wait()
    {
        Assert.Empty(new WaitTime(TimeProvider.System).Validate(Parse("""{ "type": "wait.time", "durationMs": 60000 }""")));
    }
}
