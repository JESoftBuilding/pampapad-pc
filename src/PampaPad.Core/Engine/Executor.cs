using System.Text.Json;

namespace PampaPad.Core;

public enum StepOutcome
{
    Ok,
    Failed,
    /// <summary>Lo salteó su onlyIf. Sin uso hasta la etapa 5.</summary>
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
/// Corre los pasos de una acción en serie y corta en la primera falla, diciendo número y tipo de paso (Decidido 8).
/// Antes de empezar valida la acción entera: con un problema no corre nada (02 §4.4).
/// </summary>
public sealed class Executor(IReadOnlyList<IStepType> catalog, TimeProvider clock)
{
    /// <summary>Tope global por acción (01 §4.5). Propuesto, sin medir.</summary>
    public static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(60);

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
            else if (step.OnlyIf is { } condition)
            {
                // Las condiciones entran en la etapa 5. Hasta entonces, saltear en silencio un "sólo si" sería correr lo que el usuario quiso cuidar.
                found.Add(new Problem($"{where}.onlyIf", ProblemCodes.ConditionUnknown, $"Condición desconocida: \"{condition.Type}\". Esta acción no corre.", Severity.Warning));
            }
            else
            {
                found.AddRange(type.Validate(step).Select(problem => problem with { Where = problem.Where.Length == 0 ? where : $"{where}.{problem.Where}" }));
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

        using var timeout = new CancellationTokenSource(ActionTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        for (int i = 0; i < steps.Count; i++)
        {
            Step step = steps[i].Item;
            StepResult result;
            try
            {
                linked.Token.ThrowIfCancellationRequested();

                // WaitAsync: un paso que no mira el token no puede retener la acción más allá del tope.
                result = await Find(step.Type)!.RunAsync(step, invocation, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = new StepResult(new Failure(ErrorCodes.Cancelled, timeout.IsCancellationRequested ? "Pasó el tope de 60 s de la acción." : "Se canceló."));
            }
            catch (Exception e)
            {
                // No es una falla esperada: vuelve como internal_error y el Dispatcher la deja en el registro.
                result = new StepResult(new Failure(ErrorCodes.InternalError, $"El paso reventó: {e.Message}"));
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

    private IStepType? Find(string type) => catalog.FirstOrDefault(candidate => candidate.Id == type);
}
