using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;
using PadAction = PampaPad.Core.Action;

namespace PampaPad.Tests;

public class ExecutorTests
{
    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.5", Gesture.Tap, 0);

    private static PadAction Chain(string steps) =>
        JsonSerializer.Deserialize<PadAction>($$"""{ "id": "prueba", "name": "Prueba", "steps": {{steps}} }""", Json.Options)!;

    private static Task<StepResult> Done(string? message = null) => Task.FromResult(new StepResult(null, message));

    private static Task<StepResult> Fails(string code) => Task.FromResult(new StepResult(new Failure(code, "No anduvo.")));

    private static IEnumerable<StepOutcome> Outcomes(ActionResult result) => result.Steps.Select(step => step.Outcome);

    [Fact]
    public async Task Runs_the_steps_in_order_and_reports_each_one()
    {
        var order = new List<string>();
        var a = new FakeStep("a") { Behavior = (_, _, _) => { order.Add("a"); return Done("hecho a"); } };
        var b = new FakeStep("b") { Behavior = (_, _, _) => { order.Add("b"); return Done(); } };
        var executor = new Executor([a, b], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a" }, { "type": "b" }, { "type": "a" } ]"""), Tap, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "a", "b", "a" }, order);
        Assert.Null(result.StepNumber);
        Assert.Null(result.Failure);
        Assert.Equal(new[] { StepOutcome.Ok, StepOutcome.Ok, StepOutcome.Ok }, Outcomes(result));
        Assert.Equal("hecho a", result.Steps[0].Message);
        Assert.Equal("prueba", result.ActionId);
        Assert.Equal(Tap.RunId, result.RunId);
        Assert.Equal(Trigger.Hotkey, result.RequestedBy);
    }

    [Fact]
    public async Task Stops_at_the_first_failure_and_says_which_step()
    {
        var a = new FakeStep("a");
        var b = new FakeStep("b") { Behavior = (_, _, _) => Fails(ErrorCodes.MarkerPaused) };
        var executor = new Executor([a, b], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a" }, { "type": "b" }, { "type": "a" } ]"""), Tap, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(2, result.StepNumber);
        Assert.Equal("b", result.StepType);
        Assert.Equal(ErrorCodes.MarkerPaused, result.Failure?.Code);
        Assert.Equal("steps[1]", result.Failure?.Where);
        Assert.Equal(new[] { StepOutcome.Ok, StepOutcome.Failed, StepOutcome.NotRun }, Outcomes(result));
        Assert.Single(a.Runs);
    }

    [Fact]
    public async Task An_unknown_step_type_runs_nothing_at_all()
    {
        var a = new FakeStep("a");
        var executor = new Executor([a], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a" }, { "type": "obs.scene", "scene": "Banco" } ]"""), Tap, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Equal("steps[1].type", result.Failure?.Where);
        Assert.Contains("obs.scene", result.Failure?.Message);
        Assert.Equal(2, result.StepNumber);
        Assert.Equal("obs.scene", result.StepType);
        Assert.Equal(new[] { StepOutcome.NotRun, StepOutcome.NotRun }, Outcomes(result));
        Assert.Empty(a.Runs);
    }

    [Fact]
    public async Task A_step_with_onlyIf_is_an_unknown_condition_and_does_not_run()
    {
        var a = new FakeStep("a");
        var executor = new Executor([a], TimeProvider.System);
        PadAction action = Chain("""[ { "type": "a", "onlyIf": { "type": "window.exists", "title": "bancocam" } } ]""");

        Problem problem = Assert.Single(executor.Check(action));
        ActionResult result = await executor.RunAsync(action, Tap, CancellationToken.None);

        Assert.Equal("steps[0].onlyIf", problem.Where);
        Assert.Equal(ProblemCodes.ConditionUnknown, problem.Code);
        Assert.Contains("window.exists", problem.Message);
        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Empty(a.Runs);
    }

    [Fact]
    public void Check_puts_the_place_of_the_step_before_what_its_type_reports()
    {
        var a = new FakeStep("a")
        {
            Validation = _ => [new Problem("marker", ProblemCodes.ParameterInvalid, "No es un tipo de marca.", Severity.Warning), new Problem("", ProblemCodes.ParameterInvalid, "Roto entero.", Severity.Warning)],
        };
        var executor = new Executor([a], TimeProvider.System);

        IReadOnlyList<Problem> problems = executor.Check(Chain("""[ { "type": "a" }, { "type": "a" } ]"""));

        Assert.Equal(new[] { "steps[0].marker", "steps[0]", "steps[1].marker", "steps[1]" }, problems.Select(problem => problem.Where));
    }

    [Fact]
    public void Check_reports_a_step_without_type_and_an_unknown_type()
    {
        var executor = new Executor([new FakeStep("a")], TimeProvider.System);

        IReadOnlyList<Problem> problems = executor.Check(Chain("""[ { "marker": "short" }, { "type": "lights" } ]"""));

        Assert.Equal(2, problems.Count);
        Assert.Equal(("steps[0].type", ProblemCodes.FieldMissing, Severity.Error), (problems[0].Where, problems[0].Code, problems[0].Severity));
        Assert.Equal(("steps[1].type", ProblemCodes.StepTypeUnknown, Severity.Warning), (problems[1].Where, problems[1].Code, problems[1].Severity));
    }

    [Fact]
    public async Task Cancelling_between_steps_stops_the_chain()
    {
        using var cancel = new CancellationTokenSource();
        var a = new FakeStep("a") { Behavior = (_, _, _) => { cancel.Cancel(); return Done(); } };
        var executor = new Executor([a], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a" }, { "type": "a" } ]"""), Tap, cancel.Token);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal(2, result.StepNumber);
        Assert.Equal(new[] { StepOutcome.Ok, StepOutcome.Failed }, Outcomes(result));
        Assert.Single(a.Runs);
    }

    [Fact]
    public async Task An_action_is_cut_at_sixty_seconds()
    {
        var clock = new FakeTimeProvider();
        var stuck = new FakeStep("stuck")
        {
            Behavior = async (_, _, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new StepResult(null);
            },
        };
        var executor = new Executor([stuck], clock);

        Task<ActionResult> running = executor.RunAsync(Chain("""[ { "type": "stuck" } ]"""), Tap, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(running.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        ActionResult result = await running;

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Contains("60 s", result.Failure?.Message);
        Assert.Equal(TimeSpan.FromSeconds(60), result.Duration);
    }

    [Fact]
    public async Task A_step_that_ignores_cancellation_does_not_hold_the_action()
    {
        var clock = new FakeTimeProvider();
        var never = new TaskCompletionSource<StepResult>();
        var deaf = new FakeStep("deaf") { Behavior = (_, _, _) => never.Task };
        var executor = new Executor([deaf], clock);

        Task<ActionResult> running = executor.RunAsync(Chain("""[ { "type": "deaf" } ]"""), Tap, CancellationToken.None);
        clock.Advance(Executor.ActionTimeout);
        ActionResult result = await running;

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
    }

    [Fact]
    public async Task A_step_that_throws_is_an_internal_error_not_a_crash()
    {
        var broken = new FakeStep("broken") { Behavior = (_, _, _) => throw new InvalidOperationException("roto a propósito") };
        var executor = new Executor([broken], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "broken" } ]"""), Tap, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.InternalError, result.Failure?.Code);
        Assert.Contains("roto a propósito", result.Failure?.Message);
        Assert.Equal(1, result.StepNumber);
    }

    [Fact]
    public async Task What_a_step_brings_back_is_copied_to_its_report()
    {
        JsonElement data = JsonSerializer.SerializeToElement(new { outputActive = true });
        var a = new FakeStep("a") { Behavior = (_, _, _) => Task.FromResult(new StepResult(null, "última línea", data)) };
        var executor = new Executor([a], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a" } ]"""), Tap, CancellationToken.None);

        StepReport report = Assert.Single(result.Steps);
        Assert.Equal("última línea", report.Message);
        Assert.True(report.Data?.GetProperty("outputActive").GetBoolean());
    }

    [Fact]
    public async Task An_action_without_steps_ends_well_and_does_nothing()
    {
        var executor = new Executor([], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("[]"), Tap, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Empty(result.Steps);
    }
}
