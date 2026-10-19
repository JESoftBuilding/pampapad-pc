using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;
using PadAction = PampaPad.Core.Action;

namespace PampaPad.Tests;

public class ExecutorOnlyIfTests
{
    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.3", Gesture.Tap, 0);

    private static PadAction Chain(string steps) =>
        JsonSerializer.Deserialize<PadAction>($$"""{ "id": "ir-al-banco", "name": "Ir al banco", "steps": {{steps}} }""", Json.Options)!;

    private static IEnumerable<StepOutcome> Outcomes(ActionResult result) => result.Steps.Select(step => step.Outcome);

    [Fact]
    public async Task A_condition_that_is_not_met_omits_only_its_step_and_the_chain_goes_on()
    {
        var a = new FakeStep("a");
        var b = new FakeStep("b");
        var executor = new Executor([a, b], [FakeCondition.NotMet("window.exists").Type], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a", "onlyIf": { "type": "window.exists" } }, { "type": "b" } ]"""), Tap, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Empty(a.Runs);
        Assert.Single(b.Runs);
        Assert.Equal(new[] { StepOutcome.Omitted, StepOutcome.Ok }, Outcomes(result));
        Assert.Equal("Salteado por su \"sólo si\" (window.exists): No se cumple.", result.Steps[0].Message);
    }

    [Fact]
    public async Task A_condition_that_is_met_runs_the_step()
    {
        var a = new FakeStep("a");
        var executor = new Executor([a], [FakeCondition.Met("window.exists").Type], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a", "onlyIf": { "type": "window.exists" } } ]"""), Tap, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Single(a.Runs);
        Assert.Equal(new[] { StepOutcome.Ok }, Outcomes(result));
    }

    [Fact]
    public async Task Negate_turns_a_met_condition_into_an_omitted_step()
    {
        var a = new FakeStep("a");
        var executor = new Executor([a], [FakeCondition.Met("window.exists").Type], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a", "onlyIf": { "type": "window.exists", "negate": true } } ]"""), Tap, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Empty(a.Runs);
        Assert.Equal(new[] { StepOutcome.Omitted }, Outcomes(result));
    }

    [Fact]
    public async Task A_condition_that_could_not_be_evaluated_fails_the_step_with_its_code_and_stops_the_chain()
    {
        var a = new FakeStep("a");
        var b = new FakeStep("b");
        var executor = new Executor([a, b], [FakeCondition.Unknowable("obs.micNotMuted", ErrorCodes.ObsDisconnected).Type], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a", "onlyIf": { "type": "obs.micNotMuted" } }, { "type": "b" } ]"""), Tap, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
        Assert.Equal("steps[0].onlyIf", result.Failure?.Where);
        Assert.StartsWith("No se pudo saber si correr el paso (obs.micNotMuted)", result.Failure?.Message, StringComparison.Ordinal);
        Assert.Equal(1, result.StepNumber);
        Assert.Equal(new[] { StepOutcome.Failed, StepOutcome.NotRun }, Outcomes(result));
        Assert.Empty(a.Runs);
        Assert.Empty(b.Runs);
    }

    [Theory]
    [InlineData("\"grabando\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[ { \"type\": \"window.exists\" } ]")]
    public async Task An_onlyIf_that_is_not_an_object_is_a_problem_and_the_action_does_not_run(string onlyIf)
    {
        var a = new FakeStep("a");
        var executor = new Executor([a], [FakeCondition.Met("window.exists").Type], TimeProvider.System);
        PadAction action = Chain($$"""[ { "type": "a", "onlyIf": {{onlyIf}} } ]""");

        Problem problem = Assert.Single(executor.Check(action));
        ActionResult result = await executor.RunAsync(action, Tap, CancellationToken.None);

        Assert.Equal("steps[0].onlyIf", problem.Where);
        Assert.Equal(ProblemCodes.ConditionUnknown, problem.Code);
        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Empty(a.Runs);
    }

    [Fact]
    public async Task A_negate_that_is_not_a_boolean_is_a_problem_and_the_action_does_not_run()
    {
        var a = new FakeStep("a");
        var executor = new Executor([a], [FakeCondition.Met("window.exists").Type], TimeProvider.System);
        PadAction action = Chain("""[ { "type": "a", "onlyIf": { "type": "window.exists", "negate": "true" } } ]""");

        Problem problem = Assert.Single(executor.Check(action));
        ActionResult result = await executor.RunAsync(action, Tap, CancellationToken.None);

        Assert.Equal("steps[0].onlyIf.negate", problem.Where);
        Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
        Assert.False(result.Ok);
        Assert.Empty(a.Runs);
    }

    [Fact]
    public void The_problems_of_the_step_and_of_its_condition_are_both_reported()
    {
        var a = new FakeStep("a") { Validation = _ => [new Problem("script", ProblemCodes.ParameterInvalid, "Falta.", Severity.Warning)] };
        var condition = new FakeCondition("window.exists", new Field("title", "Título", FieldKind.Window));
        var executor = new Executor([a], [condition.Type], TimeProvider.System);

        IReadOnlyList<Problem> problems = executor.Check(Chain("""[ { "type": "a", "onlyIf": { "type": "window.exists" } } ]"""));

        Assert.Equal(new[] { "steps[0].script", "steps[0].onlyIf.title" }, problems.Select(problem => problem.Where));
    }

    [Fact]
    public async Task An_unknown_condition_still_stops_the_action_with_a_catalog()
    {
        var a = new FakeStep("a");
        var executor = new Executor([a], [FakeCondition.Met("window.exists").Type], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a", "onlyIf": { "type": "window.existe" } } ]"""), Tap, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Contains("window.existe", result.Failure?.Message, StringComparison.Ordinal);
        Assert.Empty(a.Runs);
    }

    [Fact]
    public async Task Cancelling_while_the_condition_is_evaluated_is_cancelled()
    {
        var a = new FakeStep("a");
        using var cancel = new CancellationTokenSource();
        CancellationToken seen = default;
        var slow = new FakeCondition("obs.cameraNotCovered")
        {
            Answer = async (_, ct) =>
            {
                seen = ct;
                cancel.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
                return new Verdict(true, "");
            },
        };
        var executor = new Executor([a], [slow.Type], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a", "onlyIf": { "type": "obs.cameraNotCovered" } } ]"""), Tap, cancel.Token);

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Empty(a.Runs);

        // La condición se entera del corte: una de OBS deja de preguntar en vez de seguir sola (revisión de E5-4).
        Assert.True(seen.IsCancellationRequested);
    }

    [Fact]
    public async Task A_condition_that_ignores_cancellation_does_not_hold_the_action()
    {
        var clock = new FakeTimeProvider();
        var a = new FakeStep("a");
        var never = new TaskCompletionSource<Result<Verdict>>();
        CancellationToken seen = default;
        var deaf = new FakeCondition("obs.micNotMuted")
        {
            Answer = (_, ct) =>
            {
                seen = ct;
                return never.Task;
            },
        };
        var executor = new Executor([a], [deaf.Type], clock);

        Task<ActionResult> running = executor.RunAsync(Chain("""[ { "type": "a", "onlyIf": { "type": "obs.micNotMuted" } } ]"""), Tap, CancellationToken.None);
        clock.Advance(Executor.ActionTimeout);
        ActionResult result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal("Pasó el tope de 60 s de la acción.", result.Failure?.Message);
        Assert.Empty(a.Runs);

        // La condición se entera del tope, no sólo del corte de afuera: con ct en vez de linked.Token esto da rojo.
        Assert.True(seen.IsCancellationRequested);
    }

    [Fact]
    public async Task A_condition_that_returns_cancelled_after_the_cap_fired_gets_the_cap_text_and_keeps_its_own()
    {
        var clock = new FakeTimeProvider();
        var a = new FakeStep("a");
        var polite = new FakeCondition("obs.micNotMuted")
        {
            Answer = (_, _) =>
            {
                clock.Advance(Executor.ActionTimeout);
                return Task.FromResult<Result<Verdict>>(new Failure(ErrorCodes.Cancelled, "Se canceló esperando la respuesta de OBS."));
            },
        };
        var executor = new Executor([a], [polite.Type], clock);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a", "onlyIf": { "type": "obs.micNotMuted" } } ]"""), Tap, CancellationToken.None);

        Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code);
        Assert.Equal(
            "No se pudo saber si correr el paso (obs.micNotMuted): Pasó el tope de 60 s de la acción. Se canceló esperando la respuesta de OBS.",
            result.Failure?.Message);
        Assert.Equal("steps[0].onlyIf", result.Failure?.Where);
        Assert.Empty(a.Runs);
    }

    [Fact]
    public async Task Each_step_knows_the_id_of_its_action()
    {
        string? seen = "nada";
        var a = new FakeStep("a") { Behavior = (_, invocation, _) => { seen = invocation.ActionId; return Task.FromResult(new StepResult(null)); } };
        var executor = new Executor([a], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a" } ]"""), Tap, CancellationToken.None);

        Assert.Equal("ir-al-banco", seen);
        Assert.Equal(Tap.RunId, result.RunId);
        Assert.Null(Tap.ActionId);
    }
}
