using System.Text.Json;

namespace PampaPad.Core;

/// <summary>
/// La forma de escribir los parámetros de un paso o de una condición (02 §3, reglas 2 y 8): primero los campos que declara su tipo, en el
/// orden de sus Field; después lo que el tipo no declara, en el orden en que se leyó; y ningún null. La página arma los pasos como objetos
/// de JavaScript y puede mandar las claves en otro orden: sin esto, guardar sin cambiar nada reordenaría líneas del archivo y git mostraría
/// un diff que nadie hizo. Un paso de un tipo que el programa no conoce no se toca, ni los null de sus parámetros: no se sabe qué quiere.
/// </summary>
public static class ConfigCanon
{
    public static Config Order(Config config, IReadOnlyList<IStepType> steps, IReadOnlyList<ConditionType> conditions)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(conditions);
        return config with
        {
            Actions = config.Actions is null ? null : [.. config.Actions.Select(action => OrderAction(action, steps, conditions))],
            Preflight = config.Preflight is null ? null : [.. config.Preflight.Select(check => OrderCheck(check, conditions))],
        };
    }

    private static Action OrderAction(Action action, IReadOnlyList<IStepType> steps, IReadOnlyList<ConditionType> conditions) =>
        action is null || action.Steps is null ? action! : action with { Steps = [.. action.Steps.Select(step => OrderStep(step, steps, conditions))] };

    private static PreflightCheck OrderCheck(PreflightCheck check, IReadOnlyList<ConditionType> conditions) =>
        check is null || check.Condition is null ? check! : check with { Condition = OrderCondition(check.Condition, conditions) };

    private static Step OrderStep(Step step, IReadOnlyList<IStepType> steps, IReadOnlyList<ConditionType> conditions)
    {
        if (step is null)
        {
            return step!;
        }

        IStepType? type = steps.FirstOrDefault(candidate => candidate.Id == step.Type);
        return step with
        {
            Parameters = type is null ? step.Parameters : Ordered(step.Parameters, type.Fields),
            OnlyIf = step.OnlyIf is null ? null : OrderCondition(step.OnlyIf, conditions),
        };
    }

    private static Condition OrderCondition(Condition condition, IReadOnlyList<ConditionType> conditions)
    {
        ConditionType? type = conditions.FirstOrDefault(candidate => candidate.Id == condition.Type);
        return type is null ? condition : condition with { Parameters = Ordered(condition.Parameters, type.Fields) };
    }

    private static Dictionary<string, JsonElement> Ordered(Dictionary<string, JsonElement> parameters, IReadOnlyList<Field> fields)
    {
        var ordered = new Dictionary<string, JsonElement>();
        foreach (Field field in fields)
        {
            if (parameters.TryGetValue(field.Name, out JsonElement value) && value.ValueKind != JsonValueKind.Null)
            {
                ordered[field.Name] = value;
            }
        }

        foreach ((string name, JsonElement value) in parameters)
        {
            if (!ordered.ContainsKey(name) && value.ValueKind != JsonValueKind.Null)
            {
                ordered[name] = value;
            }
        }

        return ordered;
    }
}
