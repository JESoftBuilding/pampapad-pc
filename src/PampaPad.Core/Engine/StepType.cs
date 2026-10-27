using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// Base de los tipos de paso: deserializa los parámetros a P, comprueba que estén los campos obligatorios
/// y le deja a cada tipo sus reglas propias (01 §6).
/// </summary>
public abstract class StepType<P> : IStepType
    where P : class
{
    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract string Group { get; }

    public abstract IReadOnlyList<Field> Fields { get; }

    public IReadOnlyList<Problem> Validate(Step step) => Validate(step, parameters => Check(parameters));

    public IReadOnlyList<Problem> Validate(Step step, Config config) => Validate(step, parameters => Check(parameters, config));

    private List<Problem> Validate(Step step, Func<P, IEnumerable<Problem>> check)
    {
        var found = new List<Problem>();
        foreach (Field field in Fields)
        {
            if (!field.Optional && !step.Parameters.ContainsKey(field.Name))
            {
                found.Add(Invalid(field.Name, $"Falta \"{field.Name}\"."));
            }
        }

        if (found.Count > 0)
        {
            return found;
        }

        P? parameters = Read(step, out string error);
        if (parameters is null)
        {
            found.Add(Invalid("", error));
        }
        else
        {
            found.AddRange(check(parameters));
        }

        return found;
    }

    public Task<StepResult> RunAsync(Step step, Invocation invocation, CancellationToken ct)
    {
        P? parameters = Read(step, out string error);
        return parameters is null
            ? Task.FromResult(new StepResult(new Failure(ErrorCodes.ConfigInvalid, error)))
            : RunAsync(parameters, invocation, ct);
    }

    protected static Problem Invalid(string where, string message) => new(where, ProblemCodes.ParameterInvalid, message, Severity.Warning);

    /// <summary>Las reglas propias del tipo sobre parámetros que ya se pudieron leer.</summary>
    protected virtual IEnumerable<Problem> Check(P p) => [];

    /// <summary>Las mismas reglas contra una configuración candidata. Sólo las pisa quien mira la configuración (page.go); si no, valen las de Check(p).</summary>
    protected virtual IEnumerable<Problem> Check(P p, Config config) => Check(p);

    protected abstract Task<StepResult> RunAsync(P p, Invocation invocation, CancellationToken ct);

    private static P? Read(Step step, out string error)
    {
        error = "";
        try
        {
            P? parameters = JsonSerializer.SerializeToElement(step.Parameters, Json.Options).Deserialize<P>(Json.Options);
            if (parameters is null)
            {
                error = "El paso no tiene parámetros.";
            }

            return parameters;
        }
        catch (JsonException e)
        {
            error = $"Un parámetro no es del tipo esperado: {e.Path}.";
            return null;
        }
    }
}
