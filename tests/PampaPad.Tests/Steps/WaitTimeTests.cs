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
        // 10 s: con el reloj real no termina dentro del WaitAsync de 5 s. Task.Delay con el reloj falso no sigue adentro de Advance
        // sino en el pool: antes de mirar se le dan 200 ms reales para terminar, si fuera a terminar antes (revisión de E5-8).
        var clock = new FakeTimeProvider();
        var wait = new WaitTime(clock);

        Task<StepResult> running = wait.RunAsync(Parse("""{ "type": "wait.time", "durationMs": 10000 }"""), Tap, CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(9999));
        bool early = await Task.WhenAny(running, Task.Delay(TimeSpan.FromMilliseconds(200))) == running;
        clock.Advance(TimeSpan.FromMilliseconds(1));
        StepResult result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(early);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task Cancelling_the_step_while_it_waits_comes_out_as_cancellation()
    {
        // La de abajo pasa por el Executor, que corta igual con su WaitAsync: esta mira que la espera del paso reciba el token.
        using var cancel = new CancellationTokenSource();
        Task<StepResult> running = new WaitTime(new FakeTimeProvider()).RunAsync(Parse("""{ "type": "wait.time", "durationMs": 10000 }"""), Tap, cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
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
    [InlineData("""{ "type": "wait.time", "durationMs": 60000 }""")]
    [InlineData("""{ "type": "wait.time", "durationMs": "500" }""")]
    [InlineData("""{ "type": "wait.time", "durationMs": 1.5 }""")]
    public void A_duration_that_is_not_from_1_ms_to_under_the_action_limit_is_a_problem(string json)
    {
        // 60.000 es el tope de la acción: la acción vence antes de que la espera termine bien (02 §5, decisión 35).
        Problem problem = Assert.Single(new WaitTime(TimeProvider.System).Validate(Parse(json)));

        Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
    }

    [Fact]
    public void The_longest_wait_is_one_ms_under_the_limit_of_the_action()
    {
        Assert.Empty(new WaitTime(TimeProvider.System).Validate(Parse("""{ "type": "wait.time", "durationMs": 59999 }""")));
    }

    [Fact]
    public void The_problem_at_the_limit_says_up_to_where_it_goes_and_why()
    {
        // Es lo que se lee en el editor con la tecla rota, y el número es el de la decisión 35 (revisión de E5-23).
        Problem problem = Assert.Single(new WaitTime(TimeProvider.System).Validate(Parse("""{ "type": "wait.time", "durationMs": 60000 }""")));

        Assert.Equal("durationMs va en milisegundos, de 1 a 59999: con 60000 vence antes el tope de la acción.", problem.Message);
    }
}
