using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;
using PadAction = PampaPad.Core.Action;

namespace PampaPad.Tests;

// Lo que las pruebas del plan dejaban sin atar de la validación y del informe: qué número y qué ubicación lleva una falla
// cuando hay diez pasos o huecos en la lista, qué conserva el informe de un paso que falló y cómo se casan los tipos de paso.
// El tope, la cancelación y las ejecuciones a la vez están en ExecutorCancelTests.
public class ExecutorEdgeTests
{
    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Hotkey, "hotkeys", "key.5", Gesture.Tap, 0);

    private static PadAction Parse(string json) => JsonSerializer.Deserialize<PadAction>(json, Json.Options)!;

    private static PadAction Chain(string steps) => Parse($$"""{ "id": "prueba", "name": "Prueba", "steps": {{steps}} }""");

    private static Task<StepResult> Done() => Task.FromResult(new StepResult(null));

    private static IEnumerable<StepOutcome> Outcomes(ActionResult result) => result.Steps.Select(step => step.Outcome);

    private static string Steps(int count, int at, string special) =>
        "[" + string.Join(",", Enumerable.Range(0, count).Select(i => i == at ? special : """{ "type": "a" }""")) + "]";

    [Fact]
    public async Task An_action_without_id_or_steps_ends_well_with_an_empty_id()
    {
        var executor = new Executor([], TimeProvider.System);
        PadAction action = Parse("{}");

        ActionResult result = await executor.RunAsync(action, Tap, CancellationToken.None);

        Assert.Empty(executor.Check(action));
        Assert.True(result.Ok);
        Assert.Equal("", result.ActionId);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public async Task An_unknown_step_after_the_tenth_is_found_by_its_own_number()
    {
        var a = new FakeStep("a");
        var executor = new Executor([a], new FakeTimeProvider());

        ActionResult result = await executor.RunAsync(Chain(Steps(12, 10, """{ "type": "lights" }""")), Tap, CancellationToken.None);

        Assert.Equal(11, result.StepNumber);
        Assert.Equal("lights", result.StepType);
        Assert.Equal("steps[10].type", result.Failure?.Where);
        Assert.StartsWith("La acción tiene un problema y no corre.", result.Failure?.Message);
        Assert.Contains("lights", result.Failure?.Message);
        Assert.Equal(12, result.Steps.Count);
        Assert.All(result.Steps, step => Assert.Equal(StepOutcome.NotRun, step.Outcome));
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Empty(a.Runs);
    }

    [Fact]
    public async Task A_failure_at_the_eleventh_step_says_eleven_and_the_ten_before_it_are_ok()
    {
        var a = new FakeStep("a");
        var b = new FakeStep("b") { Behavior = (_, _, _) => Task.FromResult(new StepResult(new Failure(ErrorCodes.MarkerPaused, "Pausa."))) };
        var executor = new Executor([a, b], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain(Steps(13, 10, """{ "type": "b" }""")), Tap, CancellationToken.None);

        Assert.Equal(11, result.StepNumber);
        Assert.Equal("steps[10]", result.Failure?.Where);
        Assert.Equal(10, a.Runs.Count);
        Assert.Equal(Enumerable.Repeat(StepOutcome.Ok, 10).Append(StepOutcome.Failed).Concat(Enumerable.Repeat(StepOutcome.NotRun, 2)), Outcomes(result));
    }

    [Fact]
    public async Task A_null_in_the_list_is_skipped_but_the_numbers_are_the_ones_of_the_file()
    {
        var a = new FakeStep("a");
        var b = new FakeStep("b") { Behavior = (_, _, _) => Task.FromResult(new StepResult(new Failure(ErrorCodes.MarkerPaused, "Pausa."))) };
        var executor = new Executor([a, b], TimeProvider.System);
        PadAction action = Chain("""[ null, { "type": "a" }, null, { "type": "b" } ]""");

        ActionResult result = await executor.RunAsync(action, Tap, CancellationToken.None);

        Assert.Empty(executor.Check(action));
        Assert.Equal(4, result.StepNumber);
        Assert.Equal("b", result.StepType);
        Assert.Equal("steps[3]", result.Failure?.Where);
        Assert.Equal(new[] { StepOutcome.Ok, StepOutcome.Failed }, Outcomes(result));
    }

    [Fact]
    public async Task A_failed_step_keeps_its_message_and_data_in_the_report_and_its_text_in_the_failure()
    {
        JsonElement data = JsonSerializer.SerializeToElement(new { outputActive = false });
        var b = new FakeStep("b")
        {
            Behavior = (_, _, _) => Task.FromResult(new StepResult(new Failure(ErrorCodes.MarkerPaused, "Estás en pausa.", "otro"), "última línea", data)),
        };
        var executor = new Executor([b], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "b" } ]"""), Tap, CancellationToken.None);

        StepReport report = Assert.Single(result.Steps);
        Assert.Equal(StepOutcome.Failed, report.Outcome);
        Assert.Equal("última línea", report.Message);
        Assert.False(report.Data?.GetProperty("outputActive").GetBoolean());
        Assert.Equal(ErrorCodes.MarkerPaused, result.Failure?.Code);
        Assert.Equal("Estás en pausa.", result.Failure?.Message);
        Assert.Equal("steps[0]", result.Failure?.Where);
    }

    [Fact]
    public void An_unknown_condition_is_reported_alone_and_a_missing_or_unknown_type_comes_first()
    {
        var a = new FakeStep("a") { Validation = _ => [new Problem("x", ProblemCodes.ParameterInvalid, "No.", Severity.Warning)] };
        var executor = new Executor([a], TimeProvider.System);

        Problem known = Assert.Single(executor.Check(Chain("""[ { "type": "a", "onlyIf": { "type": "window.exists" } } ]""")));
        Problem unknown = Assert.Single(executor.Check(Chain("""[ { "type": "lights", "onlyIf": { "type": "window.exists" } } ]""")));
        Problem missing = Assert.Single(executor.Check(Chain("""[ { "onlyIf": { "type": "window.exists" } } ]""")));

        Assert.Equal(ProblemCodes.ConditionUnknown, known.Code);
        Assert.Equal(ProblemCodes.StepTypeUnknown, unknown.Code);
        Assert.Equal(ProblemCodes.FieldMissing, missing.Code);
    }

    [Fact]
    public void A_step_type_has_to_match_whole_and_with_the_same_case()
    {
        var marker = new FakeStep("obs.marker");
        var executor = new Executor([marker], TimeProvider.System);

        foreach (string type in new[] { "obs", "obs.marker.x", "Obs.Marker", "OBS.MARKER", "obs.marke" })
        {
            Problem problem = Assert.Single(executor.Check(Chain($$"""[ { "type": "{{type}}" } ]""")));
            Assert.Equal(ProblemCodes.StepTypeUnknown, problem.Code);
        }

        Assert.Empty(executor.Check(Chain("""[ { "type": "obs.marker" } ]""")));
        Assert.Empty(marker.Runs);
    }

    [Fact]
    public async Task A_step_gets_its_own_step_and_the_invocation_it_was_launched_with()
    {
        Step? seenStep = null;
        Invocation? seenInvocation = null;
        var a = new FakeStep("a")
        {
            Behavior = (step, invocation, _) =>
            {
                seenStep = step;
                seenInvocation = invocation;
                return Done();
            },
        };
        var executor = new Executor([a], TimeProvider.System);
        PadAction action = Chain("""[ { "type": "a", "marker": "short" } ]""");

        await executor.RunAsync(action, Tap, CancellationToken.None);

        Assert.Same(action.Steps![0], seenStep);
        Assert.Equal(Tap with { ActionId = "prueba" }, seenInvocation);     // el Executor le suma el id de la acción (E5-4)
    }

    [Fact]
    public async Task A_step_that_throws_after_waiting_is_an_internal_error_and_the_rest_do_not_run()
    {
        var broken = new FakeStep("broken")
        {
            Behavior = async (_, _, _) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("roto después de esperar");
            },
        };
        var after = new FakeStep("after");
        var executor = new Executor([broken, after], TimeProvider.System);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "broken" }, { "type": "after" } ]"""), Tap, CancellationToken.None);

        Assert.Equal(ErrorCodes.InternalError, result.Failure?.Code);
        Assert.Equal("El paso reventó: roto después de esperar", result.Failure?.Message);
        Assert.Equal("steps[0]", result.Failure?.Where);
        Assert.Equal(new[] { StepOutcome.Failed, StepOutcome.NotRun }, Outcomes(result));
        Assert.Empty(after.Runs);
    }

    [Fact]
    public async Task The_duration_is_what_the_clock_measured_between_start_and_end()
    {
        var clock = new FakeTimeProvider();
        clock.Advance(TimeSpan.FromHours(3));
        var a = new FakeStep("a")
        {
            Behavior = (_, _, _) =>
            {
                clock.Advance(TimeSpan.FromSeconds(5));
                return Done();
            },
        };
        var executor = new Executor([a], clock);

        ActionResult result = await executor.RunAsync(Chain("""[ { "type": "a" } ]"""), Tap, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(5), result.Duration);
    }
}
