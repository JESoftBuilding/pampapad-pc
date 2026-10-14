namespace PampaPad.Core;

/// <summary>
/// Del evento de entrada a la acción (01 §3.1 y §4.6). Cada pulsación lanza su propia tarea; dos acciones distintas corren a la vez
/// y la misma acción no se pisa. Además es quien sabe qué página rige: por eso le aplica la página a cada fuente de entrada
/// y junta en el estado todos los problemas de la configuración.
/// </summary>
public sealed class Dispatcher
{
    private readonly Library _library;
    private readonly StateStore _state;
    private readonly Executor _executor;
    private readonly Log _log;
    private readonly Lock _gate = new();
    private readonly Lock _refreshing = new();
    private readonly Dictionary<string, Run> _running = [];
    private readonly List<IInputSource> _sources = [];

    public Dispatcher(Library library, StateStore state, Executor executor, Log log)
    {
        _library = library;
        _state = state;
        _executor = executor;
        _log = log;
        _library.Changed += _ => Refresh();
        Refresh();
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

    /// <summary>Vuelve a calcular lo que depende de la biblioteca: página base, problemas y lo que cada fuente escucha.</summary>
    public void Refresh()
    {
        lock (_refreshing)
        {
            Config config = _library.Current;
            string main = config.ResolveMainPage() ?? "";
            IReadOnlyList<string> cycle = [.. config.Pages.Indexed().Where(page => page.Item.Show != false && page.Item.Id is not null).Select(page => page.Item.Id!)];

            // En esta etapa no hay page.go ni capa: la base es siempre la principal. La regla de grabación ya pasa por acá.
            NavigationResult navigation = Navigation.Resolve(new NavigationInput(main, null, _state.Current.Obs.Recording, main, null, cycle));

            var problems = new List<Problem>(_library.Problems);
            foreach ((int index, Action action) in config.Actions.Indexed())
            {
                problems.AddRange(CheckAction(action, index));
            }

            (int pageIndex, Page? page) = config.Pages.Indexed().FirstOrDefault(candidate => navigation.BasePage.Length > 0 && candidate.Item.Id == navigation.BasePage);
            IInputSource[] sources;
            lock (_gate)
            {
                sources = [.. _sources];
            }

            // Sin biblioteca la configuración está vacía: la página vacía hace que cada fuente suelte todo.
            foreach (IInputSource source in sources)
            {
                problems.AddRange(ApplyTo(source, page ?? new Page(), config.Hotkeys ?? [], pageIndex));
            }

            // El validador y el ejecutor informan los dos un paso sin "type": queda una vez.
            IReadOnlyList<Problem> all = [.. problems.DistinctBy(problem => (problem.Where, problem.Code))];
            _state.Update(s => s with
            {
                Library = new LibraryState(_library.Status, _library.Folder),
                Problems = all,
                BasePage = navigation.BasePage,
                ActivePage = navigation.ActivePage,
            });
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

    /// <summary>Llega en el hilo de la fuente, así que sólo busca la acción y la lanza. Nunca tira.</summary>
    public void Receive(InputEvent e)
    {
        try
        {
            // Press, Release y Turn son de la consola y pasan por Gestures (etapa 3b). Los atajos entregan el toque ya reconocido.
            if (e.Gesture != Gesture.Tap)
            {
                return;
            }

            Config config = _library.Current;
            string? actionId = null;
            if (e.Control == Controls.Hotkey)
            {
                actionId = config.Hotkeys is { } general && e.Value >= 0 && e.Value < general.Count ? general[e.Value]?.Action : null;
            }
            else if (Controls.TryKey(e.Control, out int position))
            {
                // Siempre la acción del toque, nunca la de onHold: por teclado no existe "mantener" (02 §6).
                actionId = config.FindPage(_state.Current.BasePage)?.Keys.Indexed().Select(key => key.Item).FirstOrDefault(key => key.Position == position)?.Action;
            }

            if (actionId is null)
            {
                return;
            }

            Trigger trigger = e.Source == HotkeySource.SourceId ? Trigger.Hotkey : Trigger.Key;
            _ = Launch(actionId, new Invocation(Guid.NewGuid(), trigger, e.Source, e.Control, e.Gesture, e.Value));
        }
        catch (Exception error)
        {
            _log.Error("dispatcher", $"No se pudo atender {e.Control}: {error.Message}", ErrorCodes.InternalError);
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
