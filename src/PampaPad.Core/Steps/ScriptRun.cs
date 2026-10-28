namespace PampaPad.Core;

public sealed record ScriptRunParameters(string? Script, bool? Wait, int? TimeoutSec);

/// <summary>
/// script.run: corre un script de la biblioteca por su id, según el contrato de 02 §9 (02 §5). wait y timeoutSec pisan los de la ficha.
/// Devuelve como mensaje la última línea no vacía de la salida. Un script que falta, sin ficha o que no se puede correr es un problema
/// del paso (02 §10, problema 6, con el código de la falla): la acción no corre, como con cualquier problema (02 §4.4), aunque su onlyIf
/// lo fuera a saltear. Se mira en el disco en cada revisión.
/// </summary>
public sealed class ScriptRun(ScriptRunner runner, Library library, ScriptOutputSink? output = null) : StepType<ScriptRunParameters>
{
    public const string TypeId = "script.run";

    public override string Id => TypeId;

    public override string Name => "Correr un script";

    public override string Group => "Cadenas";

    public override IReadOnlyList<Field> Fields { get; } =
    [
        new Field("script", "Script", FieldKind.Script),
        new Field("wait", "Esperar a que termine", FieldKind.Boolean, Optional: true),
        new Field("timeoutSec", "Tope, en segundos", FieldKind.Number, Optional: true),
    ];

    protected override IEnumerable<Problem> Check(ScriptRunParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.Script))
        {
            yield return Invalid("script", "Falta el id del script, como el nombre de su carpeta en scripts/.");
            yield break;
        }

        if (library.ReadScript(p.Script) is { Ok: false, Failure: { } missing })
        {
            yield return new Problem("script", missing.Code, missing.Message, Severity.Warning);
        }

        // Tope exclusivo (decisión 35): con 60 vence antes el de la acción y la falla sale cancelled, no script_timeout.
        if (p.TimeoutSec is <= 0 || p.TimeoutSec >= Executor.ActionTimeout.TotalSeconds)
        {
            yield return Invalid("timeoutSec", $"timeoutSec va en segundos, de 1 a {Executor.ActionTimeout.TotalSeconds - 1:0}: con {Executor.ActionTimeout.TotalSeconds:0} vence antes el tope de la acción.");
        }
    }

    protected override async Task<StepResult> RunAsync(ScriptRunParameters p, Invocation invocation, CancellationToken ct)
    {
        TimeSpan? timeout = p.TimeoutSec is int seconds ? TimeSpan.FromSeconds(seconds) : null;
        var request = new ScriptRequest(p.Script ?? "", p.Wait, timeout, invocation);

        // La salida en vivo es sólo de una prueba de la ventana (scripts.test y action.test): una acción guardada no la publica.
        if (invocation.RequestedBy == Trigger.Window && output is { } sink)
        {
            Guid runId = invocation.RunId;
            request = request with
            {
                OnLine = line => sink.Line(runId, line.TrimStart(ScriptRequest.ErrorMark[0]), line.StartsWith(ScriptRequest.ErrorMark, StringComparison.Ordinal)),
                OnFinished = end => sink.End(runId, new ScriptEnd(end.Ok, end.ExitCode, (long)end.Duration.TotalMilliseconds, end.Message)),
            };
        }

        Result<ScriptResult> result = await runner.RunAsync(request, ct).ConfigureAwait(false);
        return result.Ok ? new StepResult(null, result.Value!.Message) : new StepResult(result.Failure);
    }
}
