using System.Text.Json;

namespace PampaPad.Core;

public enum StepOutcome
{
    Ok,
    Failed,

    /// <summary>Lo salteó su onlyIf (Decidido 15): no es una falla y no avisa.</summary>
    Omitted,

    /// <summary>La cadena se cortó antes de llegar.</summary>
    NotRun,
}

/// <summary>Cómo le fue a un paso, con lo que haya devuelto. Hay uno por paso, en orden.</summary>
public readonly record struct StepReport(StepOutcome Outcome, string? Message = null, JsonElement? Data = null);

/// <summary>El resultado de una ejecución. StepNumber cuenta desde 1: es el número que se le muestra al usuario.</summary>
public sealed record ActionResult(
    string ActionId,
    Guid RunId,
    Trigger RequestedBy,
    bool Ok,
    int? StepNumber,
    string? StepType,
    Failure? Failure,
    TimeSpan Duration,
    IReadOnlyList<StepReport> Steps);

/// <summary>
/// Corre los pasos de una acción en serie y corta en la primera falla, diciendo número y tipo de paso (Decidido 8). Antes de empezar
/// valida la acción entera: con un problema no corre nada (02 §4.4). Un paso con onlyIf corre sólo si su condición se cumple; si no,
/// queda omitido y la cadena sigue (Decidido 15). Una condición que no se pudo evaluar hace fallar el paso con su código (02 §4.6;
/// pregunta abierta 43, escrito (a)).
/// </summary>
public sealed class Executor(IReadOnlyList<IStepType> catalog, IReadOnlyList<ConditionType> conditions, TimeProvider clock)
{
    /// <summary>Tope global por acción (01 §4.5). Propuesto, sin medir.</summary>
    public static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(60);

    private static readonly string TimeoutText = $"Pasó el tope de {ActionTimeout.TotalSeconds:0} s de la acción.";

    /// <summary>Sin catálogo de condiciones: todo onlyIf es una condición desconocida y su acción no corre.</summary>
    public Executor(IReadOnlyList<IStepType> catalog, TimeProvider clock)
        : this(catalog, [], clock)
    {
    }

    /// <summary>Los problemas de una acción que dependen del catálogo. Where es relativo a la acción: steps[0].marker.</summary>
    public IReadOnlyList<Problem> Check(Action action)
    {
        var found = new List<Problem>();
        foreach ((int index, Step step) in action.Steps.Indexed())
        {
            string where = $"steps[{index}]";
            if (step.Type.Length == 0)
            {
                found.Add(new Problem($"{where}.type", ProblemCodes.FieldMissing, "Falta \"type\".", Severity.Error));
            }
            else if (Find(step.Type) is not { } type)
            {
                found.Add(new Problem($"{where}.type", ProblemCodes.StepTypeUnknown, $"No conozco el tipo de paso \"{step.Type}\": esta acción no corre.", Severity.Warning));
            }
            else if (step.Parameters.ContainsKey("onlyIf"))
            {
                // Un onlyIf que no es un objeto queda en Parameters (Step.cs, el lector) y el paso correría sin su "sólo si" (hallazgo E3-1).
                found.Add(new Problem($"{where}.onlyIf", ProblemCodes.ConditionUnknown, "onlyIf va como un objeto con su type, por ejemplo { \"type\": \"window.exists\", \"title\": \"bancocam\" }. Esta acción no corre.", Severity.Warning));
            }
            else if (step.OnlyIf is { } condition && FindCondition(condition.Type) is null)
            {
                // Saltear en silencio un "sólo si" que no se entiende sería correr lo que el usuario quiso cuidar.
                found.Add(new Problem($"{where}.onlyIf", ProblemCodes.ConditionUnknown, $"Condición desconocida: \"{condition.Type}\". Esta acción no corre.", Severity.Warning));
            }
            else
            {
                found.AddRange(type.Validate(step).Select(problem => problem with { Where = Join(where, problem.Where) }));
                if (step.OnlyIf is { } guard)
                {
                    found.AddRange(ConditionRules.Validate(FindCondition(guard.Type)!, guard).Select(problem => problem with { Where = Join($"{where}.onlyIf", problem.Where) }));
                }
            }
        }

        return found;
    }

    public async Task<ActionResult> RunAsync(Action action, Invocation invocation, CancellationToken ct)
    {
        long started = clock.GetTimestamp();
        List<(int Index, Step Item)> steps = [.. action.Steps.Indexed()];
        var reports = new StepReport[steps.Count];
        Array.Fill(reports, new StepReport(StepOutcome.NotRun));

        ActionResult Finish(int? at, Failure? failure) => new(
            action.Id ?? "",
            invocation.RunId,
            invocation.RequestedBy,
            failure is null,
            at is null ? null : steps[at.Value].Index + 1,
            at is null ? null : steps[at.Value].Item.Type,
            failure,
            clock.GetElapsedTime(started),
            reports);

        if (Check(action) is [Problem first, ..])
        {
            int broken = steps.FindIndex(step => first.Where.StartsWith($"steps[{step.Index}]", StringComparison.Ordinal));
            return Finish(broken < 0 ? null : broken, new Failure(ErrorCodes.ConfigInvalid, $"La acción tiene un problema y no corre. {first.Message}", first.Where));
        }

        // Los pasos saben de qué acción son: script.run se lo pasa al script (02 §9, punto 3).
        Invocation running = invocation with { ActionId = action.Id ?? "" };
        using var timeout = new CancellationTokenSource(ActionTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        for (int i = 0; i < steps.Count; i++)
        {
            Step step = steps[i].Item;
            StepResult result;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                if (step.OnlyIf is { } condition)
                {
                    ConditionResult guard = await ConditionRules.EvaluateAsync(FindCondition(condition.Type)!, condition, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
                    if (guard.Outcome == ConditionOutcome.Fail)
                    {
                        reports[i] = new StepReport(StepOutcome.Omitted, $"Salteado por su \"sólo si\" ({condition.Type}): {guard.Detail}");
                        continue;
                    }

                    if (guard.Failure is { } unknown)
                    {
                        // skipped corta la cadena con el código de lo que no dejó evaluar (02 §4.6; pregunta abierta 43, escrito (a)).
                        reports[i] = new StepReport(StepOutcome.Failed, guard.Detail);
                        return Finish(i, unknown with
                        {
                            Message = $"No se pudo saber si correr el paso ({condition.Type}): {unknown.Message}",
                            Where = $"steps[{steps[i].Index}].onlyIf",
                        });
                    }
                }

                // WaitAsync: un paso que no mira el token no puede retener la acción más allá del tope.
                result = await Find(step.Type)!.RunAsync(step, running, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = new StepResult(new Failure(ErrorCodes.Cancelled, timeout.IsCancellationRequested ? TimeoutText : "Se canceló."));
            }
            catch (Exception e)
            {
                // No es una falla esperada: vuelve como internal_error y el Dispatcher la deja en el registro.
                result = new StepResult(new Failure(ErrorCodes.InternalError, $"El paso reventó: {e.Message}"));
            }

            // Un paso que devuelve cancelled por su cuenta (ObsClient devuelve el corte en vez de tirarlo) no sabe quién cortó:
            // el motivo lo conoce el ejecutor. Se antepone y se conserva lo que el paso dijo de lo suyo.
            if (result.Failure is { Code: ErrorCodes.Cancelled } cut && timeout.IsCancellationRequested && cut.Message != TimeoutText)
            {
                result = result with { Failure = cut with { Message = $"{TimeoutText} {cut.Message}" } };
            }

            if (result.Failure is { } failure)
            {
                reports[i] = new StepReport(StepOutcome.Failed, result.Message, result.Data);
                return Finish(i, failure with { Where = $"steps[{steps[i].Index}]" });
            }

            reports[i] = new StepReport(StepOutcome.Ok, result.Message, result.Data);
        }

        return Finish(null, null);
    }

    private static string Join(string prefix, string where) => where.Length == 0 ? prefix : $"{prefix}.{where}";

    private IStepType? Find(string type) => catalog.FirstOrDefault(candidate => candidate.Id == type);

    private ConditionType? FindCondition(string type) => conditions.FirstOrDefault(candidate => candidate.Id == type);
}
