using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Un tipo de condición de mentira: contesta lo que la prueba le diga y cuenta cuántas veces la evaluaron.</summary>
internal sealed class FakeCondition
{
    private int _evaluations;

    public FakeCondition(string id, params Field[] fields)
    {
        Type = new ConditionType(id, id, fields, (p, ct) =>
        {
            Interlocked.Increment(ref _evaluations);
            return Answer(p, ct);
        });
    }

    public ConditionType Type { get; }

    public int Evaluations => Volatile.Read(ref _evaluations);

    public Func<JsonElement, CancellationToken, Task<Result<Verdict>>> Answer { get; set; } =
        (_, _) => Task.FromResult<Result<Verdict>>(new Verdict(true, "Se cumple."));

    public static FakeCondition Met(string id) => new(id);

    public static FakeCondition NotMet(string id) =>
        new(id) { Answer = (_, _) => Task.FromResult<Result<Verdict>>(new Verdict(false, "No se cumple.")) };

    public static FakeCondition Unknowable(string id, string code) =>
        new(id) { Answer = (_, _) => Task.FromResult<Result<Verdict>>(new Failure(code, "No se pudo preguntar.")) };
}
