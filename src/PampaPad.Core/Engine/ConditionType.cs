using System.Text.Json;

namespace PampaPad.Core;

/// <summary>Lo que contesta una condición evaluada: si se cumple y qué midió contra qué esperaba (02 §4.6 y §4.9).</summary>
public readonly record struct Verdict(bool Met, string Detail)
{
    /// <summary>Lo que no se aplica (su fuente no está al aire, no está activa o no es del tipo que mira) se da por cumplido (03 §7).</summary>
    public static Verdict NotApplicable(string why) => new(true, $"No aplica: {why}");
}

/// <summary>pass: evaluó y se cumple; fail: evaluó y no se cumple; skipped: no se pudo evaluar (02 §4.6).</summary>
public enum ConditionOutcome
{
    Pass,
    Fail,
    Skipped,
}

/// <summary>Una condición ya evaluada, con su negate aplicado. Failure va sólo con skipped: es lo que no dejó evaluarla.</summary>
public sealed record ConditionResult(ConditionOutcome Outcome, string Detail, Failure? Failure = null);

/// <summary>
/// Un tipo de condición del catálogo (01 §3.1 y §6): sirve a la vez para un onlyIf y para el chequeo previo. Evaluate recibe los
/// parámetros como un objeto JSON y devuelve el veredicto o la falla que no la dejó evaluar. Check son las reglas propias de los
/// parámetros, además de que estén los obligatorios; Where es relativo a la condición.
/// </summary>
public sealed record ConditionType(string Id, string Name, IReadOnlyList<Field> Fields, Func<JsonElement, CancellationToken, Task<Result<Verdict>>> Evaluate)
{
    public Func<JsonElement, IEnumerable<Problem>> Check { get; init; } = _ => [];
}
