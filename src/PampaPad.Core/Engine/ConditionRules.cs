using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// Lo que es igual para todas las condiciones: validar sus parámetros, evaluarla con su negate y leer lo que trae. negate invierte el
/// veredicto tal como vino, también un "no aplica"; nunca toca un skipped, que no es ni sí ni no (02 §4.6, con el sí de Emmanuel en E5-1).
/// </summary>
public static class ConditionRules
{
    public const string NegateField = "negate";

    /// <summary>Los problemas de una condición de un tipo conocido. Where es relativo a la condición: title, negate o vacío.</summary>
    public static IReadOnlyList<Problem> Validate(ConditionType type, Condition condition)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(condition);
        var found = new List<Problem>();

        // Un negate que no es booleano queda en Parameters (Condition.cs, el lector): sin esto la condición correría sin invertirse (hallazgo E3-1).
        if (condition.Parameters.ContainsKey(NegateField))
        {
            found.Add(Invalid(NegateField, "negate va sin comillas: true o false."));
        }

        foreach (Field field in type.Fields)
        {
            if (!field.Optional && !condition.Parameters.ContainsKey(field.Name))
            {
                found.Add(Invalid(field.Name, $"Falta \"{field.Name}\"."));
            }
        }

        if (found.Count == 0)
        {
            found.AddRange(type.Check(Parameters(condition)));
        }

        return found;
    }

    /// <summary>Evalúa y aplica negate. Una falla de Evaluate es skipped con esa falla. La cancelación sale como excepción, como la de un paso.</summary>
    public static async Task<ConditionResult> EvaluateAsync(ConditionType type, Condition condition, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(condition);
        Result<Verdict> verdict;
        try
        {
            verdict = await type.Evaluate(Parameters(condition), ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new ConditionResult(ConditionOutcome.Skipped, $"La condición {type.Id} reventó: {error.Message}",
                new Failure(ErrorCodes.InternalError, $"La condición {type.Id} reventó: {error.Message}"));
        }

        if (!verdict.Ok)
        {
            return new ConditionResult(ConditionOutcome.Skipped, verdict.Failure.Message, verdict.Failure);
        }

        bool met = verdict.Value.Met != (condition.Negate == true);
        return new ConditionResult(met ? ConditionOutcome.Pass : ConditionOutcome.Fail, verdict.Value.Detail);
    }

    /// <summary>Los parámetros como un objeto, para Evaluate y Check.</summary>
    public static JsonElement Parameters(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return JsonSerializer.SerializeToElement(condition.Parameters, Json.Options);
    }

    public static string? Text(JsonElement parameters, string name) =>
        ObsMessages.Has(parameters, name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Un entero escrito como número, sin decimales; cualquier otra cosa es null.</summary>
    public static int? Integer(JsonElement parameters, string name) =>
        ObsMessages.Has(parameters, name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;

    /// <summary>Una referencia a OBS: el texto solo o { uuid, name } (02 §4.8). Null si falta o no se entiende.</summary>
    public static ObsRef? Reference(JsonElement parameters, string name)
    {
        if (!ObsMessages.Has(parameters, name, out JsonElement value))
        {
            return null;
        }

        try
        {
            ObsRef found = value.Deserialize<ObsRef>(Json.Options);
            return string.IsNullOrWhiteSpace(found.Name) ? null : found;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Problem Invalid(string where, string message) => new(where, ProblemCodes.ParameterInvalid, message, Severity.Warning);
}
