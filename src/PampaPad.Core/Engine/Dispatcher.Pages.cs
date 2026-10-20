namespace PampaPad.Core;

// Qué página rige (02 §7). Navigation decide; acá se guarda la base que eligió page.go, se le aplica la página a cada fuente y se dispara
// la regla 2. Está aparte por el tope de 300 líneas (01 §1).
public sealed partial class Dispatcher
{
    // El type de una página de teclas (02 §4.2); sin type también es de teclas.
    private const string KeysPageType = "keys";

    private IReadOnlyList<Problem> _configProblems = [];

    // La base que eligió page.go; null es la principal. Se escribe sólo bajo _refreshing.
    private string? _chosen;

    /// <summary>
    /// Cambia la página base: es lo que hace page.go (02 §5). Con una toma en curso falla con navigation_blocked y la base sigue en la
    /// principal (regla 1). Una página que no existe, o que no es de teclas, falla con config_invalid sin cambiar nada. Vuelve a aplicar
    /// la página a las fuentes: los atajos de tecla pasan a ser los de la nueva base (regla 6).
    /// </summary>
    public Failure? Go(string target)
    {
        lock (_refreshing)
        {
            Config config = _library.Current;
            string main = config.ResolveMainPage() ?? "";
            if (target is not (Navigation.Main or Navigation.Next or Navigation.Previous))
            {
                if (config.FindPage(target) is not { } page)
                {
                    return new Failure(ErrorCodes.ConfigInvalid, $"La página \"{target}\" no existe: no se cambió de página.");
                }

                // Lo mismo que el recorrido (Cycle), por id: la local la muestra la consola sola (regla 7) y un tipo reservado o desconocido
                // se saltea (02 §4.2).
                if (!IsKeys(page))
                {
                    return new Failure(ErrorCodes.ConfigInvalid, $"La página \"{target}\" es de tipo {page.Type}, que no es de teclas: no se cambió de página.");
                }
            }

            NavigationResult result = Navigation.Resolve(new NavigationInput(_chosen ?? main, null, _state.Current.Obs.Recording, main, target, Cycle(config)));
            if (result.Failure is { } blocked)
            {
                return blocked;
            }

            _chosen = result.BasePage == main ? null : result.BasePage;
            ApplyPage(config);
            _log.Info("dispatcher", $"Página base: {result.BasePage}.");
            return null;
        }
    }

    // La regla 2 (02 §7): si la grabación arranca con otra página de base, se vuelve a la principal en el acto, y queda ahí. El suscriptor
    // del estado sólo encola (StateStore): aplicar la página registra atajos, que espera al hilo de Windows.
    private void OnStateChanged(AppState before, AppState after)
    {
        if (before.Obs.Recording == RecordingState.Stopped && after.Obs.Recording != RecordingState.Stopped && Volatile.Read(ref _chosen) is not null)
        {
            ThreadPool.QueueUserWorkItem(_ => ReturnToMain());
        }
    }

    private void ReturnToMain()
    {
        try
        {
            lock (_refreshing)
            {
                if (_chosen is not { } from || _state.Current.Obs.Recording == RecordingState.Stopped)
                {
                    return;
                }

                _chosen = null;
                ApplyPage(_library.Current);
                _log.Info("dispatcher", $"La grabación arrancó con la página {from} de base: se vuelve a la principal (02 §7, regla 2).");
            }
        }
        catch (Exception error)
        {
            _log.Error("dispatcher", $"No se pudo volver a la página principal: {error.Message}", ErrorCodes.InternalError);
        }
    }

    // Calcula la página que rige y se la aplica a cada fuente. Sólo bajo _refreshing. Grabando, o si la página elegida ya no está en el
    // archivo o dejó de ser de teclas, la base vuelve a ser la principal.
    private void ApplyPage(Config config)
    {
        string main = config.ResolveMainPage() ?? "";
        RecordingState recording = _state.Current.Obs.Recording;
        if (recording != RecordingState.Stopped || (_chosen is not null && (config.FindPage(_chosen) is not { } chosen || !IsKeys(chosen))))
        {
            _chosen = null;
        }

        NavigationResult navigation = Navigation.Resolve(new NavigationInput(_chosen ?? main, null, recording, main, null, Cycle(config)));
        (int pageIndex, Page? page) = config.Pages.Indexed().FirstOrDefault(candidate => navigation.BasePage.Length > 0 && candidate.Item.Id == navigation.BasePage);
        IInputSource[] sources;
        lock (_gate)
        {
            sources = [.. _sources];
        }

        // Sin biblioteca la configuración está vacía: la página vacía hace que cada fuente suelte todo.
        var problems = new List<Problem>(_configProblems);
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
            MainPage = main,
        });
    }

    // La página cuyas teclas valen ahora. Grabando es la principal aunque la base del estado no haya cambiado todavía (regla 1).
    private static string KeysPage(Config config, AppState now) =>
        Navigation.Resolve(new NavigationInput(now.BasePage, null, now.Obs.Recording, config.ResolveMainPage() ?? "", null, [])).ActivePage;

    // Las páginas del recorrido de next y previous: las que no tienen show en falso, en su orden (02 §7, regla 5). Sólo las de teclas:
    // la local no está en el recorrido (regla 7) y un tipo reservado o desconocido se saltea (02 §4.2).
    private static IReadOnlyList<string> Cycle(Config config) =>
        [.. config.Pages.Indexed()
            .Where(page => page.Item.Show != false && page.Item.Id is not null && IsKeys(page.Item))
            .Select(page => page.Item.Id!)];

    // Una página de teclas: type ausente o keys (02 §4.2). Las únicas que pueden ser la base. PageGo.Check marca, al cargar, un target que no
    // lo es (Decidido 35).
    internal static bool IsKeys(Page page) => page.Type is null or KeysPageType;
}
