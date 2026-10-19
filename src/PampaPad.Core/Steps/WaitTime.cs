namespace PampaPad.Core;

public sealed record WaitTimeParameters(int? DurationMs);

/// <summary>
/// wait.time: espera ese tiempo (02 §5). Falla sólo si se cancela o vence el tope de la acción, con cancelled: la cancelación sale como
/// excepción y el Executor la traduce, con el motivo. Una espera más larga que el tope de la acción nunca terminaría bien: es un problema
/// del paso, como 0 o un negativo (02 §5: de 1 a 60.000 ms, decisión 32).
/// </summary>
public sealed class WaitTime(TimeProvider clock) : StepType<WaitTimeParameters>
{
    public const string TypeId = "wait.time";

    public override string Id => TypeId;

    public override string Name => "Esperar un rato";

    public override string Group => "Cadenas";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        new Field("durationMs", "Cuánto, en milisegundos", FieldKind.Duration),
    ];

    protected override IEnumerable<Problem> Check(WaitTimeParameters p)
    {
        if (p.DurationMs is not > 0 || p.DurationMs > Executor.ActionTimeout.TotalMilliseconds)
        {
            yield return Invalid("durationMs", $"durationMs va en milisegundos, de 1 a {Executor.ActionTimeout.TotalMilliseconds:0}: más que el tope de la acción no termina nunca.");
        }
    }

    protected override async Task<StepResult> RunAsync(WaitTimeParameters p, Invocation invocation, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(p.DurationMs ?? 0), clock, ct).ConfigureAwait(false);
        return new StepResult(null);
    }
}
