using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public class ConditionRulesTests
{
    private static Condition Parse(string json) => JsonSerializer.Deserialize<Condition>(json, Json.Options)!;

    [Theory]
    [InlineData(true, null, ConditionOutcome.Pass)]
    [InlineData(false, null, ConditionOutcome.Fail)]
    [InlineData(true, false, ConditionOutcome.Pass)]
    [InlineData(true, true, ConditionOutcome.Fail)]
    [InlineData(false, true, ConditionOutcome.Pass)]
    public async Task Negate_inverts_the_verdict(bool met, bool? negate, ConditionOutcome expected)
    {
        var fake = new FakeCondition("prueba") { Answer = (_, _) => Task.FromResult<Result<Verdict>>(new Verdict(met, "Midió algo.")) };
        string written = negate is { } value ? $$""", "negate": {{(value ? "true" : "false")}}""" : "";

        ConditionResult result = await ConditionRules.EvaluateAsync(fake.Type, Parse($$"""{ "type": "prueba"{{written}} }"""), CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal("Midió algo.", result.Detail);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task Negate_also_inverts_what_does_not_apply()
    {
        var fake = new FakeCondition("prueba") { Answer = (_, _) => Task.FromResult<Result<Verdict>>(Verdict.NotApplicable("la cámara no está al aire")) };

        ConditionResult plain = await ConditionRules.EvaluateAsync(fake.Type, Parse("""{ "type": "prueba" }"""), CancellationToken.None);
        ConditionResult negated = await ConditionRules.EvaluateAsync(fake.Type, Parse("""{ "type": "prueba", "negate": true }"""), CancellationToken.None);

        Assert.Equal(ConditionOutcome.Pass, plain.Outcome);
        Assert.Equal("No aplica: la cámara no está al aire", plain.Detail);
        Assert.Equal(ConditionOutcome.Fail, negated.Outcome);
    }

    [Fact]
    public async Task What_could_not_be_evaluated_is_skipped_with_its_failure_and_negate_does_not_touch_it()
    {
        FakeCondition fake = FakeCondition.Unknowable("prueba", ErrorCodes.ObsDisconnected);

        ConditionResult result = await ConditionRules.EvaluateAsync(fake.Type, Parse("""{ "type": "prueba", "negate": true }"""), CancellationToken.None);

        Assert.Equal(ConditionOutcome.Skipped, result.Outcome);
        Assert.Equal(ErrorCodes.ObsDisconnected, result.Failure?.Code);
    }

    [Fact]
    public async Task A_condition_that_throws_is_skipped_as_an_internal_error()
    {
        var fake = new FakeCondition("prueba") { Answer = (_, _) => throw new InvalidOperationException("rota") };

        ConditionResult result = await ConditionRules.EvaluateAsync(fake.Type, Parse("""{ "type": "prueba" }"""), CancellationToken.None);

        Assert.Equal(ConditionOutcome.Skipped, result.Outcome);
        Assert.Equal(ErrorCodes.InternalError, result.Failure?.Code);
    }

    [Fact]
    public async Task Cancelling_comes_out_as_an_exception_like_a_step()
    {
        var fake = new FakeCondition("prueba") { Answer = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new Verdict(true, ""); } };
        using var cut = new CancellationTokenSource();
        cut.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConditionRules.EvaluateAsync(fake.Type, Parse("""{ "type": "prueba" }"""), cut.Token));
    }

    [Theory]
    [InlineData("""{ "type": "prueba", "title": "bancocam", "negate": "true" }""", "negate")]
    [InlineData("""{ "type": "prueba", "title": "bancocam", "negate": 1 }""", "negate")]
    [InlineData("""{ "type": "prueba" }""", "title")]
    public void A_negate_that_is_not_a_boolean_or_a_missing_field_is_a_problem(string json, string where)
    {
        var fake = new FakeCondition("prueba", new Field("title", "Título", FieldKind.Window));

        Problem problem = Assert.Single(ConditionRules.Validate(fake.Type, Parse(json)));

        Assert.Equal(where, problem.Where);
        Assert.Equal(ProblemCodes.ParameterInvalid, problem.Code);
    }

    [Fact]
    public void The_own_rules_run_only_when_the_required_fields_are_there()
    {
        var checks = 0;
        var fake = new FakeCondition("prueba", new Field("title", "Título", FieldKind.Window));
        ConditionType type = fake.Type with { Check = _ => { checks++; return []; } };

        ConditionRules.Validate(type, Parse("""{ "type": "prueba" }"""));
        ConditionRules.Validate(type, Parse("""{ "type": "prueba", "title": "x" }"""));

        Assert.Equal(1, checks);
    }

    [Fact]
    public void The_readers_take_only_what_has_the_right_kind()
    {
        JsonElement p = ConditionRules.Parameters(Parse("""{ "type": "x", "title": "bancocam", "width": 1823, "height": "850", "half": 1.5, "source": "Captura de pantalla", "scene": { "uuid": "u", "name": "Banco" } }"""));

        Assert.Equal("bancocam", ConditionRules.Text(p, "title"));
        Assert.Null(ConditionRules.Text(p, "width"));
        Assert.Equal(1823, ConditionRules.Integer(p, "width"));
        Assert.Null(ConditionRules.Integer(p, "height"));
        Assert.Null(ConditionRules.Integer(p, "half"));
        Assert.Equal(new ObsRef(null, "Captura de pantalla"), ConditionRules.Reference(p, "source"));
        Assert.Equal(new ObsRef("u", "Banco"), ConditionRules.Reference(p, "scene"));
        Assert.Null(ConditionRules.Reference(p, "width"));
        Assert.Null(ConditionRules.Reference(p, "nada"));
    }
}
