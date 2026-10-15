using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

// La base StepType<P> con un tipo de paso de mentira, para fijar sus reglas sin depender de obs.marker:
// cada tipo nuevo hereda de acá la lectura de los parámetros y el aviso de lo que falta.
public class StepTypeTests
{
    private static readonly Invocation Tap = new(Guid.NewGuid(), Trigger.Key, "console", "key.1", Gesture.Press, 0);

    private static Step Parse(string json) => JsonSerializer.Deserialize<Step>(json, Json.Options)!;

    [Fact]
    public void Every_missing_required_field_is_a_problem_in_the_order_of_the_fields()
    {
        IReadOnlyList<Problem> problems = new Echo().Validate(Parse("""{ "type": "echo" }"""));

        Assert.Equal(new[] { "name", "mode" }, problems.Select(problem => problem.Where));
        Assert.All(problems, problem =>
        {
            Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
            Assert.Equal(Severity.Warning, problem.Severity);
        });
        Assert.Equal("Falta \"name\".", problems[0].Message);
    }

    [Fact]
    public void The_own_rules_of_the_type_wait_until_the_required_fields_are_there()
    {
        // Con "mode" ausente, Check no corre: no se acumula un problema de más sobre un campo que ni está.
        IReadOnlyList<Problem> problems = new Echo().Validate(Parse("""{ "type": "echo", "name": "malo" }"""));

        Assert.Equal("mode", Assert.Single(problems).Where);
    }

    [Fact]
    public void The_own_rules_of_the_type_run_when_the_fields_are_there()
    {
        IReadOnlyList<Problem> problems = new Echo().Validate(Parse("""{ "type": "echo", "name": "malo", "mode": "a" }"""));

        Problem problem = Assert.Single(problems);
        Assert.Equal("name", problem.Where);
        Assert.Equal("El nombre no puede ser malo.", problem.Message);
    }

    [Fact]
    public void An_optional_field_may_be_missing()
    {
        Assert.Empty(new Echo().Validate(Parse("""{ "type": "echo", "name": "bueno", "mode": "a" }""")));
    }

    [Fact]
    public void A_parameter_of_the_wrong_type_is_one_problem_that_says_where_it_is()
    {
        IReadOnlyList<Problem> problems = new Echo().Validate(Parse("""{ "type": "echo", "name": "bueno", "mode": "a", "times": "tres" }"""));

        Problem problem = Assert.Single(problems);
        Assert.Equal("", problem.Where);
        Assert.Contains("$.times", problem.Message);
        Assert.Equal(Severity.Warning, problem.Severity);
    }

    [Fact]
    public void Parameter_names_are_camel_case_and_case_sensitive_like_the_rest_of_the_file()
    {
        IReadOnlyList<Problem> problems = new Echo().Validate(Parse("""{ "type": "echo", "Name": "bueno", "mode": "a" }"""));

        Assert.Equal("Falta \"name\".", Assert.Single(problems).Message);
    }

    [Fact]
    public async Task Running_hands_the_typed_parameters_to_the_type()
    {
        StepResult result = await new Echo().RunAsync(Parse("""{ "type": "echo", "name": "ana", "mode": "a", "times": 3, "extra": true }"""), Tap, CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal("ana|a|3|key.1", result.Message);
    }

    [Fact]
    public async Task Running_with_a_parameter_of_the_wrong_type_is_config_invalid_and_the_type_does_not_run()
    {
        var echo = new Echo();

        StepResult result = await echo.RunAsync(Parse("""{ "type": "echo", "name": "ana", "mode": "a", "times": "tres" }"""), Tap, CancellationToken.None);

        Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code);
        Assert.Contains("$.times", result.Failure?.Message);
        Assert.Equal(0, echo.Runs);
    }

    [Fact]
    public async Task A_failure_of_the_type_comes_back_as_it_is()
    {
        StepResult result = await new Echo().RunAsync(Parse("""{ "type": "echo", "name": "ana", "mode": "falla" }"""), Tap, CancellationToken.None);

        Assert.Equal("echo_failed", result.Failure?.Code);
    }

    public sealed record EchoParameters(string? Name, string? Mode, int? Times);

    private sealed class Echo : StepType<EchoParameters>
    {
        public int Runs { get; private set; }

        public override string Id => "echo";

        public override string Name => "Eco";

        public override string Group => "Prueba";

        public override IReadOnlyList<Field> Fields { get; } =
        [
            new Field("name", "Nombre", FieldKind.Text),
            new Field("mode", "Modo", FieldKind.Option, Options: ["a", "falla"]),
            new Field("times", "Veces", FieldKind.Text, Optional: true),
        ];

        protected override IEnumerable<Problem> Check(EchoParameters p)
        {
            if (p.Name == "malo")
            {
                yield return Invalid("name", "El nombre no puede ser malo.");
            }
        }

        protected override Task<StepResult> RunAsync(EchoParameters p, Invocation invocation, CancellationToken ct)
        {
            Runs++;
            return Task.FromResult(p.Mode == "falla"
                ? new StepResult(new Failure("echo_failed", "Falló a propósito."))
                : new StepResult(null, $"{p.Name}|{p.Mode}|{p.Times}|{invocation.Control}"));
        }
    }
}
