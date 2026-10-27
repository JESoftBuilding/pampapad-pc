using System.Text.Json;

namespace PampaPad.Core;

/// <summary>Un tipo de paso del menú fijo (Decidido 8). El id es la clave que va en config.json.</summary>
public interface IStepType
{
    string Id { get; }

    string Name { get; }

    string Group { get; }

    IReadOnlyList<Field> Fields { get; }

    /// <summary>Los problemas de los parámetros de un paso. Where es el nombre del parámetro, relativo al paso.</summary>
    IReadOnlyList<Problem> Validate(Step step);

    /// <summary>
    /// Lo mismo contra una configuración que todavía no es la cargada, que es lo que valida config.save antes de escribir (hallazgo E5-11).
    /// Los pasos que no miran la configuración (todos menos page.go) no lo tocan: la respuesta es la de Validate(step).
    /// </summary>
    IReadOnlyList<Problem> Validate(Step step, Config config) => Validate(step);

    Task<StepResult> RunAsync(Step step, Invocation invocation, CancellationToken ct);
}

/// <summary>Lo que devuelve un paso: una falla esperada, o nada. Message y Data son para los pasos que traen algo de vuelta.</summary>
public readonly record struct StepResult(Failure? Failure, string? Message = null, JsonElement? Data = null);
