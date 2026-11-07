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

    // La página encima de page.overlay; null es ninguna (02 §7, regla 8). Se escribe sólo bajo _refreshing.
    private string? _overlay;

    // El Seq del estado que se veía al abrir o sacar la de encima: el aviso de una grabación que arrancó antes llega tarde, y no la cierra.
    private long _overlaySeq;

    /// <summary>
    /// Pone target encima de la página que se ve, o la saca si ya estaba: es lo que hace page.overlay (02 §5 y §7, regla 8). Vale grabando:
    /// no cambia la base ni las fuentes, sólo lo que se ve. Una página que no existe, o que no es de teclas, falla con config_invalid.
    /// </summary>
    public Failure? ToggleOverlay(string target)
    {
        lock (_refreshing)
        {
            Config config = _library.Current;
            if (config.FindPage(target) is not { } page)
            {
                return new Failure(ErrorCodes.ConfigInvalid, $"La página \"{target}\" no existe: no se abrió encima.");
            }

            if (!IsKeys(page))
            {
                return new Failure(ErrorCodes.ConfigInvalid, $"La página \"{target}\" es de tipo {page.Type}, que no es de teclas: no se abrió encima.");
            }

            _overlay = _overlay == target ? null : target;
            _overlaySeq = _state.Current.Seq;
            PublishActive(config);
            _log.Info("dispatcher", _overlay is null ? $"Se cerró la página encima {target}." : $"Página encima: {target}.");
            return null;
        }
    }

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
            string main = config.ResolveMainPage(_state.Current.Obs.IsOpen) ?? "";
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

            NavigationResult result = Navigation.Resolve(new NavigationInput(_chosen ?? main, HeldLayer(config), _state.Current.Obs.Recording, main, target, Cycle(config), _overlay));
            if (result.Failure is { } blocked)
            {
                return blocked;
            }

            _chosen = result.BasePage == main ? null : result.BasePage;
            _overlay = result.Overlay;
            ApplyPage(config);
            _log.Info("dispatcher", $"Página base: {result.BasePage}.");
            return null;
        }
    }

    // La regla 2 (02 §7): si la grabación arranca con otra página de base, o con una encima, se vuelve a la principal en el acto, y queda
    // ahí (regla 8). Y la regla 9: al abrirse o cerrarse OBS cambia la principal. El suscriptor del estado sólo encola (StateStore): aplicar
    // la página registra atajos, que espera al hilo de Windows.
    private void OnStateChanged(AppState before, AppState after)
    {
        if (before.Obs.Recording == RecordingState.Stopped && after.Obs.Recording != RecordingState.Stopped
            && (Volatile.Read(ref _chosen) is not null || Volatile.Read(ref _overlay) is not null))
        {
            ThreadPool.QueueUserWorkItem(_ => ReturnToMain(after.Seq));
        }

        if (before.Obs.IsOpen != after.Obs.IsOpen && _library.Current.MainPageWithoutObs is not null)
        {
            ThreadPool.QueueUserWorkItem(_ => FollowObs());
        }
    }

    // Vuelve a aplicar la página con la principal de ahora, no con la del aviso: de dos cambios seguidos, el último manda. La línea del
    // registro sale sólo si la principal cambió.
    private void FollowObs()
    {
        try
        {
            lock (_refreshing)
            {
                string before = _state.Current.MainPage;
                ApplyPage(_library.Current);
                AppState now = _state.Current;
                if (now.MainPage != before)
                {
                    _log.Info("dispatcher", $"OBS se {(now.Obs.IsOpen ? "abrió" : "cerró")}: la principal es {now.MainPage}.");
                }
            }
        }
        catch (Exception error)
        {
            _log.Error("dispatcher", $"No se pudo seguir a OBS con la página principal: {error.Message}", ErrorCodes.InternalError);
        }
    }

    // started es el Seq del cambio en que arrancó la grabación. Sólo se cierra la de encima que ya estaba: la que se abrió viéndola grabar
    // queda, aunque este aviso corra después (RGB-2).
    private void ReturnToMain(long started)
    {
        try
        {
            lock (_refreshing)
            {
                bool closes = _overlay is not null && _overlaySeq < started;
                if ((_chosen is null && !closes) || _state.Current.Obs.Recording == RecordingState.Stopped)
                {
                    return;
                }

                string from = _chosen ?? _overlay ?? "";
                _chosen = null;
                if (closes)
                {
                    _overlay = null;
                }

                ApplyPage(_library.Current);
                _log.Info("dispatcher", $"La grabación arrancó con la página {from} a la vista: se vuelve a la principal (02 §7, regla 2; con la página encima, regla 8).");
            }
        }
        catch (Exception error)
        {
            _log.Error("dispatcher", $"No se pudo volver a la página principal: {error.Message}", ErrorCodes.InternalError);
        }
    }

    // Calcula la página que rige y se la aplica a cada fuente. Sólo bajo _refreshing. Grabando, o si la página elegida ya no está en el
    // archivo o dejó de ser de teclas, la base vuelve a ser la principal. La elegida que pasa a ser la principal (regla 9) desde ahí la sigue.
    private void ApplyPage(Config config)
    {
        ObsState obs = _state.Current.Obs;
        string main = config.ResolveMainPage(obs.IsOpen) ?? "";
        RecordingState recording = obs.Recording;
        if (recording != RecordingState.Stopped || _chosen == main || (_chosen is not null && (config.FindPage(_chosen) is not { } chosen || !IsKeys(chosen))))
        {
            _chosen = null;
        }

        // La página encima que dejó de existir, o de ser de teclas, al recargar la biblioteca se va (regla 8).
        if (_overlay is not null && (config.FindPage(_overlay) is not { } overlaid || !IsKeys(overlaid)))
        {
            _overlay = null;
        }

        NavigationResult navigation = Navigation.Resolve(new NavigationInput(_chosen ?? main, HeldLayer(config), recording, main, null, Cycle(config), _overlay));
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
            OverlayPage = navigation.Overlay,
        });
    }

    // Sólo cambia la página que se ve, con la base del estado: las fuentes siguen con la base, y ni la capa ni la página encima vuelven a
    // registrar atajos (02 §7, regla 6). Bajo _refreshing.
    private void PublishActive(Config config)
    {
        string? overlay = _overlay;
        string active = KeysPage(config, _state.Current, HeldLayer(config), overlay);
        _state.Update(s => s with { ActivePage = active, OverlayPage = overlay });
    }

    // La página cuyas teclas valen ahora: la capa si se la mantiene; si no, la de encima o la base, y grabando la principal aunque la base
    // del estado no haya cambiado todavía (reglas 1, 3 y 8). Un atajo de tecla pasa overlay en null: es de la base que lo registró.
    private static string KeysPage(Config config, AppState now, string? layer, string? overlay = null) =>
        Navigation.Resolve(new NavigationInput(now.BasePage, layer, now.Obs.Recording, config.ResolveMainPage(now.Obs.IsOpen) ?? "", null, [], overlay)).ActivePage;

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
