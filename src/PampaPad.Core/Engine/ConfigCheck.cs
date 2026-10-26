namespace PampaPad.Core;

/// <summary>
/// Todos los problemas de una configuración (02 §10): los del archivo, que son del Validator, y los que dependen del catálogo de pasos y
/// de condiciones, que son del Executor. Es lo mismo que junta Dispatcher.Refresh con la configuración cargada y lo que junta config.save
/// con la candidata, antes de escribir. Un tipo de paso que tire al validar no deja sin revisar al resto: queda como un problema de su acción.
/// </summary>
public static class ConfigCheck
{
    public static IReadOnlyList<Problem> All(Config config, Executor executor, Log log)
    {
        ArgumentNullException.ThrowIfNull(config);
        return [.. Validator.Validate(config).Concat(Steps(config, executor, log)).DistinctBy(problem => (problem.Where, problem.Code))];
    }

    /// <summary>Los de cada acción, con el catálogo de pasos y contra esta misma configuración, y los de la lista preflight, con el de condiciones.</summary>
    public static IReadOnlyList<Problem> Steps(Config config, Executor executor, Log log, string module = "config")
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(log);
        var problems = new List<Problem>();
        foreach ((int index, Action action) in config.Actions.Indexed())
        {
            problems.AddRange(CheckAction(config, action, index, executor, log, module));
        }

        // La lista del chequeo previo también se revisa al cargar (02 §10, problemas 7 y 8): una condición mal escrita se ve en el ícono
        // de la bandeja, y no sólo como "sin comprobar" cuando se corre el chequeo.
        foreach ((int index, PreflightCheck check) in config.Preflight.Indexed())
        {
            problems.AddRange(CheckPreflight(check, index, executor, log, module));
        }

        return problems;
    }

    // Se revisa al arrancar y en cada recarga o guardado: un tipo de paso que tire no puede dejar al programa sin teclas ni sin arrancar.
    private static IEnumerable<Problem> CheckAction(Config config, Action action, int index, Executor executor, Log log, string module)
    {
        try
        {
            return [.. executor.Check(action, config).Select(problem => problem with { Where = $"actions[{index}].{problem.Where}" })];
        }
        catch (Exception error)
        {
            log.Error(module, $"No se pudo revisar la acción {action.Id}: {error.Message}", ErrorCodes.InternalError);
            return [new Problem($"actions[{index}]", ProblemCodes.ParameterInvalid, $"No se pudo revisar esta acción: {error.Message}", Severity.Error)];
        }
    }

    // Una comprobación de la lista preflight, con el catálogo de condiciones del ejecutor. Sin "condition" no dice nada: lo informa el validador.
    private static IEnumerable<Problem> CheckPreflight(PreflightCheck check, int index, Executor executor, Log log, string module)
    {
        if (check.Condition is not { } condition)
        {
            return [];
        }

        string where = $"preflight[{index}].condition";
        try
        {
            return [.. executor.CheckCondition(condition).Select(problem => problem with { Where = problem.Where.Length == 0 ? where : $"{where}.{problem.Where}" })];
        }
        catch (Exception error)
        {
            log.Error(module, $"No se pudo revisar la comprobación {where}: {error.Message}", ErrorCodes.InternalError);
            return [new Problem(where, ProblemCodes.ParameterInvalid, $"No se pudo revisar esta comprobación: {error.Message}", Severity.Warning)];
        }
    }
}
