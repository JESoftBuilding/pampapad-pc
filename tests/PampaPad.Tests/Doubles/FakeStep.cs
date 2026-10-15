using System.Collections.Concurrent;
using PampaPad.Core;

namespace PampaPad.Tests;

/// <summary>Un tipo de paso de mentira: anota cada corrida y hace lo que la prueba le diga.</summary>
public sealed class FakeStep(string id) : IStepType
{
    public string Id => id;

    public string Name => id;

    public string Group => "Prueba";

    public IReadOnlyList<Field> Fields => [];

    public ConcurrentQueue<Step> Runs { get; } = new();

    public Func<Step, Invocation, CancellationToken, Task<StepResult>> Behavior { get; set; } =
        (_, _, _) => Task.FromResult(new StepResult(null));

    public Func<Step, IReadOnlyList<Problem>> Validation { get; set; } = _ => [];

    public IReadOnlyList<Problem> Validate(Step step) => Validation(step);

    public Task<StepResult> RunAsync(Step step, Invocation invocation, CancellationToken ct)
    {
        Runs.Enqueue(step);
        return Behavior(step, invocation, ct);
    }
}
