using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;
using PadAction = PampaPad.Core.Action;

namespace PampaPad.Tests;

// El tope de 60 s, quién cancela y con qué texto, qué token ve el paso y que dos ejecuciones a la vez no comparten nada.
// Lo que las pruebas del plan dejaban sin atar de eso.
public class ExecutorCancelTests
{
    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.5", Gesture.Tap, 0);

    private static PadAction Parse(string json) => JsonSerializer.Deserialize<PadAction>(json, Json.Options)!;

    private static PadAction Chain(string steps) => Parse($$"""{ "id": "prueba", "name": "Prueba", "steps": {{steps}} }""");

    private static IEnumerable<StepOutcome> Outcomes(ActionResult result) => result.Steps.Select(step => step.Outcome);

    [Fact]
    public void The_cap_is_sixty_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), Executor.ActionTimeout);
    }

    [Fact]
    public async Task The_cap_covers_the_whole_action_and_not_each_step()
    {
        var clock = new FakeTimeProvider();
        var slow = new FakeStep("slow")
        {
            Behavior = async (_, _, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(40), clock, ct);
                return new StepResult(null);
            },
        };
        var executor = new Executor([slow], clock);

        Task<ActionResult> running = executor.RunAsync(Chain("""[ { "type": "slow" }, { "type": "slow" } ]"""), Tap, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(40));
        await Eventually.UntilAsync(() => slow.Runs.Count == 2, "empezó el segundo paso");
        clock.Advance(TimeSpan.FromSeconds(20));
        ActionResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal(2, result.StepNumber);
        Assert.Equal(new[] { StepOutcome.Ok, StepOutcome.Failed }, Outcomes(result));
        Assert.Equal(TimeSpan.FromSeconds(60), result.Duration);
    }

    [Fact]
    public async Task A_step_that_returns_cancelled_after_the_cap_fired_gets_the_cap_text_and_keeps_its_own()
    {
        var clock = new FakeTimeProvider();
        var polite = new FakeStep("polite")
        {
            Behavior = (_, _, _) =>
            {
                clock.Advance(Executor.ActionTimeout);
                return Task.FromResult(new StepResult(new Failure(ErrorCodes.Cancelled, "Se canceló: no se sabe si la marca entró.")));
            },
        };
        var executor = new Executor([polite], clock);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "polite" } ]"""), Tap, CancellationToken.None);

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal("Pasó el tope de 60 s de la acción. Se canceló: no se sabe si la marca entró.", result.Failure?.Message);
        Assert.Equal("steps[0]", result.Failure?.Where);
    }

    [Fact]
    public async Task A_step_that_returns_another_failure_after_the_cap_fired_keeps_it_as_it_is()
    {
        var clock = new FakeTimeProvider();
        var late = new FakeStep("late")
        {
            Behavior = (_, _, _) =>
            {
                clock.Advance(Executor.ActionTimeout);
                return Task.FromResult(new StepResult(new Failure(ErrorCodes.MarkerPaused, "Estás en pausa: la marca no entró.")));
            },
        };
        var executor = new Executor([late], clock);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "late" } ]"""), Tap, CancellationToken.None);

        Assert.Equal(ErrorCodes.MarkerPaused, result.Failure?.Code);
        Assert.Equal("Estás en pausa: la marca no entró.", result.Failure?.Message);
    }

    [Fact]
    public async Task A_step_that_returns_cancelled_when_the_caller_cancelled_keeps_its_own_text()
    {
        using var cancel = new CancellationTokenSource();
        var polite = new FakeStep("polite")
        {
            Behavior = (_, _, _) =>
            {
                cancel.Cancel();
                return Task.FromResult(new StepResult(new Failure(ErrorCodes.Cancelled, "Se canceló: no se sabe si la marca entró.")));
            },
        };
        var executor = new Executor([polite], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "polite" } ]"""), Tap, cancel.Token);

        Assert.Equal("Se canceló: no se sabe si la marca entró.", result.Failure?.Message);
    }

    [Fact]
    public async Task Cancelling_from_outside_cuts_a_deaf_step_at_once_with_its_own_text_and_the_step_saw_the_cut()
    {
        var clock = new FakeTimeProvider();
        using var cancel = new CancellationTokenSource();
        CancellationToken seen = default;
        var deaf = new FakeStep("deaf")
        {
            Behavior = (_, _, ct) =>
            {
                seen = ct;
                return new TaskCompletionSource<StepResult>().Task;
            },
        };
        var executor = new Executor([deaf], clock);

        Task<ActionResult> running = executor.RunAsync(Chain("""[ { "type": "deaf" }, { "type": "deaf" } ]"""), Tap, cancel.Token);
        Assert.False(seen.IsCancellationRequested);
        cancel.Cancel();
        ActionResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(seen.IsCancellationRequested);
        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal("Se canceló.", result.Failure?.Message);
        Assert.Equal(1, result.StepNumber);
        Assert.Equal(new[] { StepOutcome.Failed, StepOutcome.NotRun }, Outcomes(result));
        Assert.Equal(TimeSpan.Zero, result.Duration);
    }

    [Fact]
    public async Task The_cap_cancels_the_token_the_step_was_given()
    {
        var clock = new FakeTimeProvider();
        CancellationToken seen = default;
        var stuck = new FakeStep("stuck")
        {
            Behavior = (_, _, ct) =>
            {
                seen = ct;
                return new TaskCompletionSource<StepResult>().Task;
            },
        };
        var executor = new Executor([stuck], clock);

        Task<ActionResult> running = executor.RunAsync(Chain("""[ { "type": "stuck" } ]"""), Tap, CancellationToken.None);
        Assert.False(seen.IsCancellationRequested);
        clock.Advance(Executor.ActionTimeout);
        ActionResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(seen.IsCancellationRequested);
        Assert.Equal("Pasó el tope de 60 s de la acción.", result.Failure?.Message);
    }

    [Fact]
    public async Task A_cancelled_token_from_the_start_runs_no_step()
    {
        var a = new FakeStep("a");
        var executor = new Executor([a], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a" } ]"""), Tap, new CancellationToken(true));

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal(1, result.StepNumber);
        Assert.Empty(a.Runs);
    }

    [Fact]
    public async Task Two_runs_at_once_share_nothing()
    {
        var first = new TaskCompletionSource<StepResult>();
        var second = new TaskCompletionSource<StepResult>();
        var a = new FakeStep("a") { Behavior = (_, invocation, _) => invocation.Value == 1 ? first.Task : second.Task };
        var executor = new Executor([a], TimeProvider.System);
        PadAction action = Chain("""[ { "type": "a" } ]""");
        Invocation one = Tap with { RunId = Guid.NewGuid(), Value = 1 };
        Invocation two = Tap with { RunId = Guid.NewGuid(), Value = 2 };

        Task<ActionResult> runningOne = executor.RunAsync(action, one, CancellationToken.None);
        Task<ActionResult> runningTwo = executor.RunAsync(action, two, CancellationToken.None);
        second.SetResult(new StepResult(null, "segunda"));
        ActionResult resultTwo = await runningTwo.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(runningOne.IsCompleted);
        first.SetResult(new StepResult(new Failure(ErrorCodes.MarkerPaused, "Pausa.")));
        ActionResult resultOne = await runningOne.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(two.RunId, resultTwo.RunId);
        Assert.True(resultTwo.Ok);
        Assert.Equal("segunda", Assert.Single(resultTwo.Steps).Message);
        Assert.Equal(one.RunId, resultOne.RunId);
        Assert.False(resultOne.Ok);
        Assert.Equal(new[] { StepOutcome.Failed }, Outcomes(resultOne));
    }
}
