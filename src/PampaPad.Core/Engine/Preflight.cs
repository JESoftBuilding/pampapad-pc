using System.Text.Json;

namespace PampaPad.Core;

/// <summary>Una comprobación del chequeo ya evaluada: su condición, el mensaje de cuando no se cumple, el resultado y lo que midió.</summary>
public sealed record CheckResult(string Type, string Message, ConditionOutcome Outcome, string Detail, string? Code = null);

/// <summary>Un chequeo previo entero. Defaults dice si corrieron las siete del programa porque config.json no trae la lista (pregunta 14, (a)).</summary>
public sealed record PreflightReport(Guid RunId, DateTimeOffset At, Trigger RequestedBy, bool Defaults, IReadOnlyList<CheckResult> Checks)
{
    public IReadOnlyList<CheckResult> Failures => [.. Checks.Where(check => check.Outcome == ConditionOutcome.Fail)];

    /// <summary>Lo que no se pudo comprobar: figura aparte y nunca cuenta como bien (02 §4.6).</summary>
    public IReadOnlyList<CheckResult> Unchecked => [.. Checks.Where(check => check.Outcome == ConditionOutcome.Skipped)];
}

/// <summary>
/// El chequeo previo (02 §4.9; 03 §7): evalúa todas las comprobaciones, no corta en la primera, y junta lo que no pasa. Sin la lista
/// preflight en config.json corren las siete de 03 §7 sobre la escena al aire con mensajes del programa, sin escribirlas en el archivo; si
/// la lista está, corre exactamente esa (pregunta abierta 14, escrito (a)). Sólo lee. Deja una línea por comprobación en el registro y el
/// último resultado en el estado. Un chequeo nunca frena GRABAR: nadie lo consulta antes de grabar (Propuesto).
/// </summary>
public sealed class Preflight(IReadOnlyList<ConditionType> conditions, Library library, StateStore state, Log log, TimeProvider clock)
{
    /// <summary>El tope del chequeo entero (Propuesto, sin medir): cada pedido a OBS ya tiene el suyo de 2 s (03 §2).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private const string Module = "preflight";

    /// <summary>Las siete de 03 §7, en el orden de su tabla, con los mensajes del programa.</summary>
    public static IReadOnlyList<PreflightCheck> Defaults { get; } =
    [
        Default("""{ "type": "obs.captureHasMonitor" }""", "La captura de pantalla no tiene monitor"),
        Default("""{ "type": "obs.sourceHasImage" }""", "Una fuente de la escena no tiene imagen"),
        Default("""{ "type": "obs.cameraAudioOff" }""", "La cámara manda su propio audio"),
        Default("""{ "type": "obs.cameraNotCovered" }""", "La cámara está tapada o en negro"),
        Default("""{ "type": "obs.recordFormatIs", "format": "hybrid_mp4" }""", "El formato de grabación no admite marcas"),
        Default("""{ "type": "obs.micNotMuted" }""", "El micrófono está silenciado"),
        Default("""{ "type": "obs.canvasFilled" }""", "La escena no llena el lienzo"),
    ];

    public async Task<PreflightReport> RunAsync(Trigger requestedBy, CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        IReadOnlyList<PreflightCheck>? listed = library.Current.Preflight;
        var checks = new List<CheckResult>();
        using var limit = new CancellationTokenSource(Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        foreach ((int _, PreflightCheck check) in (listed ?? Defaults).Indexed())
        {
            CheckResult result;
            try
            {
                result = await CheckAsync(check, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                string why = limit.IsCancellationRequested && !ct.IsCancellationRequested ? $"pasó el tope de {Timeout.TotalSeconds:0} s del chequeo" : "se canceló";
                result = new CheckResult(check.Condition?.Type ?? "", check.Message ?? "", ConditionOutcome.Skipped, $"No se comprobó: {why}.", ErrorCodes.Cancelled);
            }

            checks.Add(result);
            log.Write(result.Outcome == ConditionOutcome.Fail ? LogLevel.Warning : LogLevel.Info, Module, Line(result), result.Code, requestedBy, runId);
        }

        var report = new PreflightReport(runId, clock.GetLocalNow(), requestedBy, listed is null, checks);
        state.Update(s => s with { Preflight = report });
        log.Write(LogLevel.Info, Module, $"Chequeo previo: {checks.Count} comprobaciones, {report.Failures.Count} con falla y {report.Unchecked.Count} sin comprobar.", null, requestedBy, runId);
        return report;
    }

    /// <summary>La línea del registro de una comprobación: qué es, cómo le fue y lo que midió.</summary>
    public static string Line(CheckResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return check.Outcome switch
        {
            ConditionOutcome.Pass => $"{check.Type}: bien. {check.Detail}",
            ConditionOutcome.Fail => $"{check.Type}: falla. {check.Message}: {check.Detail}",
            _ => $"{check.Type}: sin comprobar. {check.Detail}",
        };
    }

    private static PreflightCheck Default(string condition, string message) =>
        new() { Condition = JsonSerializer.Deserialize<Condition>(condition, Json.Options), Message = message };

    private async Task<CheckResult> CheckAsync(PreflightCheck check, CancellationToken ct)
    {
        // Las que no preguntan a OBS (el micrófono, la grabación) no mirarían el token: cortado el chequeo, ninguna se da por buena.
        ct.ThrowIfCancellationRequested();
        Condition? condition = check.Condition;
        string type = condition?.Type ?? "";
        string message = check.Message ?? type;
        if (condition is null || conditions.FirstOrDefault(candidate => candidate.Id == type) is not { } known)
        {
            return new CheckResult(type, message, ConditionOutcome.Skipped, $"No conozco la condición \"{type}\".", ErrorCodes.ConfigInvalid);
        }

        if (ConditionRules.Validate(known, condition) is [Problem first, ..])
        {
            return new CheckResult(type, message, ConditionOutcome.Skipped, first.Message, ErrorCodes.ConfigInvalid);
        }

        ConditionResult result = await ConditionRules.EvaluateAsync(known, condition, ct).ConfigureAwait(false);
        return new CheckResult(type, message, result.Outcome, result.Detail, result.Failure?.Code);
    }
}
