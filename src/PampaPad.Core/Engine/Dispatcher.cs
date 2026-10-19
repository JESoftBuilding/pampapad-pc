namespace PampaPad.Core;

/// <summary>
/// Del evento de entrada a la acción (01 §3.1 y §4.6). Cada pulsación lanza su propia tarea; dos acciones distintas corren a la vez
/// y la misma acción no se pisa. Además es quien sabe qué página rige: por eso le aplica la página a cada fuente de entrada
/// y junta en el estado todos los problemas de la configuración.
/// </summary>
public sealed partial class Dispatcher
{
    private readonly Library _library;
    private readonly StateStore _state;
    private readonly Executor _executor;
    private readonly Gestures _gestures;
    private readonly Log _log;
    private readonly Lock _gate = new();
    private readonly Lock _refreshing = new();
    private readonly Dictionary<string, Run> _running = [];
    private readonly List<IInputSource> _sources = [];

    public Dispatcher(Library library, StateStore state, Executor executor, Gestures gestures, Log log)
    {
        _library = library;
        _state = state;
        _executor = executor;
        _gestures = gestures;
        _log = log;
        _library.Changed += _ => Refresh();
        Refresh();

        // La regla 2 de navegación necesita un disparador: la grabación que arranca en otra página (02 §7; Dispatcher.Pages.cs).
        _state.Subscribe(OnStateChanged);
    }

    /// <summary>Terminó una acción, bien o mal. Una pulsación ignorada porque la acción ya estaba en curso no avisa.</summary>
    public event Action<ActionResult>? Finished;

    public void Attach(IInputSource source)
    {
        lock (_gate)
        {
            _sources.Add(source);
        }

        source.Input += Receive;
        Refresh();
    }

    /// <summary>Vuelve a calcular lo que depende de la biblioteca: problemas, página base y lo que cada fuente escucha.</summary>
    public void Refresh()
    {
        lock (_refreshing)
        {
            Config config = _library.Current;
            var problems = new List<Problem>(_library.Problems);
            foreach ((int index, Action action) in config.Actions.Indexed())
            {
                problems.AddRange(CheckAction(action, index));
            }

            // La lista del chequeo previo también se revisa al cargar (02 §10, problemas 7 y 8): una condición mal escrita se ve en el ícono
            // de la bandeja, y no sólo como "sin comprobar" cuando se corre el chequeo.
            foreach ((int index, PreflightCheck check) in config.Preflight.Indexed())
            {
                problems.AddRange(CheckPreflight(check, index));
            }

            // Los de la configuración se guardan aparte: cambiar de página vuelve a aplicar las fuentes sin volver a revisar cada acción.
            _configProblems = problems;
            ApplyPage(config);
        }
    }

    // Refresh corre en el arranque y en cada recarga: un tipo de paso o una fuente que tiren no pueden dejar al programa sin teclas ni sin arrancar.
    private IEnumerable<Problem> CheckAction(Action action, int index)
    {
        try
        {
            return [.. _executor.Check(action).Select(problem => problem with { Where = $"actions[{index}].{problem.Where}" })];
        }
        catch (Exception error)
        {
            _log.Error("dispatcher", $"No se pudo revisar la acción {action.Id}: {error.Message}", ErrorCodes.InternalError);
            return [new Problem($"actions[{index}]", ProblemCodes.ParameterInvalid, $"No se pudo revisar esta acción: {error.Message}", Severity.Error)];
        }
    }

    // Una comprobación de la lista preflight, con el catálogo de condiciones del ejecutor. Sin "condition" no dice nada: lo informa el validador.
    private IEnumerable<Problem> CheckPreflight(PreflightCheck check, int index)
    {
        if (check.Condition is not { } condition)
        {
            return [];
        }

        string where = $"preflight[{index}].condition";
        try
        {
            return [.. _executor.CheckCondition(condition).Select(problem => problem with { Where = problem.Where.Length == 0 ? where : $"{where}.{problem.Where}" })];
        }
        catch (Exception error)
        {
            _log.Error("dispatcher", $"No se pudo revisar la comprobación {where}: {error.Message}", ErrorCodes.InternalError);
            return [new Problem(where, ProblemCodes.ParameterInvalid, $"No se pudo revisar esta comprobación: {error.Message}", Severity.Warning)];
        }
    }

    private IEnumerable<Problem> ApplyTo(IInputSource source, Page page, IReadOnlyList<HotkeyBinding> hotkeys, int pageIndex)
    {
        try
        {
            return [.. source.Apply(page, hotkeys).Select(problem =>
                problem.Where.StartsWith("keys[", StringComparison.Ordinal) ? problem with { Where = $"pages[{pageIndex}].{problem.Where}" } : problem)];
        }
        catch (Exception error)
        {
            _log.Error("dispatcher", $"La fuente {source.Id} no pudo tomar sus teclas: {error.Message}", ErrorCodes.InternalError);
            return [new Problem("", ErrorCodes.InternalError, $"La fuente {source.Id} no pudo tomar sus teclas: {error.Message}", Severity.Error)];
        }
    }

    public Task<ActionResult> Launch(string actionId, Invocation invocation)
    {
        Action? action = _library.Current.FindAction(actionId);
        if (action is null)
        {
            // Finished siempre sale de un hilo del grupo: el de una fuente (el de los atajos) no se presta a lo que haga quien lo escucha.
            return Task.Run(() => Finish(Rejected(actionId, invocation, ErrorCodes.ConfigInvalid, $"La acción \"{actionId}\" no existe.")));
        }

        var run = new Run(invocation.RunId, invocation.RequestedBy, new CancellationTokenSource());
        lock (_gate)
        {
            if (_running.TryGetValue(actionId, out Run? current))
            {
                // La misma acción no se pisa (01 §4.6): queda una línea con el runId de la que está en curso, y no suena ni se muestra nada.
                _log.Write(LogLevel.Info, "dispatcher", $"{actionId} ya está en curso: se ignora la pulsación.", null, current.RequestedBy, current.RunId);
                return Task.FromResult(Rejected(actionId, invocation, ErrorCodes.Cancelled, $"La acción ya está en curso (runId {current.RunId})."));
            }

            _running[actionId] = run;
        }

        PublishRunning();
        return Task.Run(() => RunAsync(actionId, action, invocation, run));
    }

    public bool Cancel(Guid runId)
    {
        Run? found;
        lock (_gate)
        {
            found = _running.Values.FirstOrDefault(run => run.RunId == runId);
        }

        found?.Cancel.Cancel();
        return found is not null;
    }

    public void CancelAll()
    {
        Run[] all;
        lock (_gate)
        {
            all = [.. _running.Values];
        }

        // Fuera del lock: cancelar puede continuar la tarea en este mismo hilo, y esa tarea saca su entrada de _running.
        foreach (Run run in all)
        {
            run.Cancel.Cancel();
        }
    }

    private static ActionResult Rejected(string actionId, Invocation invocation, string code, string message) =>
        new(actionId, invocation.RunId, invocation.RequestedBy, false, null, null, new Failure(code, message), TimeSpan.Zero, []);

    private async Task<ActionResult> RunAsync(string actionId, Action action, Invocation invocation, Run run)
    {
        ActionResult result;
        try
        {
            result = await _executor.RunAsync(action, invocation, run.Cancel.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            result = Rejected(actionId, invocation, ErrorCodes.InternalError, $"La acción reventó: {e.Message}");
        }

        // Primero se libera la acción y después se avisa: quien reacciona al aviso ya la puede volver a lanzar.
        lock (_gate)
        {
            _running.Remove(actionId);
        }

        PublishRunning();
        return Finish(result);
    }

    private void PublishRunning() => _state.Update(s =>
    {
        // La foto se saca dentro de Update para que dos avisos cruzados no dejen en el estado una lista vieja.
        lock (_gate)
        {
            return s with { Running = [.. _running.Select(run => new RunningAction(run.Value.RunId, run.Key, run.Value.RequestedBy))] };
        }
    });

    private ActionResult Finish(ActionResult result)
    {
        if (result.Ok)
        {
            _log.Write(LogLevel.Info, "dispatcher", $"{result.ActionId} terminó bien en {result.Duration.TotalMilliseconds:0} ms.", null, result.RequestedBy, result.RunId);
        }
        else
        {
            string step = result.StepNumber is { } number ? $" en el paso {number} ({result.StepType})" : "";
            _log.Write(LogLevel.Warning, "dispatcher", $"{result.ActionId} falló{step}: {result.Failure?.Message}", result.Failure?.Code, result.RequestedBy, result.RunId);
        }

        try
        {
            Finished?.Invoke(result);
        }
        catch (Exception e)
        {
            _log.Write(LogLevel.Error, "dispatcher", $"Un suscriptor de Finished reventó: {e.Message}", ErrorCodes.InternalError, result.RequestedBy, result.RunId);
        }

        return result;
    }

    /// <summary>El CancellationTokenSource no se libera a mano: no tiene temporizador y Cancel puede llegar después de que la acción terminó.</summary>
    private sealed record Run(Guid RunId, Trigger RequestedBy, CancellationTokenSource Cancel);
}
