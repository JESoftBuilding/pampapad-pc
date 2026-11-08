using Microsoft.Win32;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>La única raíz de composición: todos los new, en el orden de 01 §4.3, y el apagado en orden inverso.</summary>
internal sealed partial class Composition : IAsyncDisposable
{
    private static readonly TimeSpan ShutdownLimit = TimeSpan.FromSeconds(3);

    private readonly CancellationTokenSource _life = new();
    private readonly List<Task> _loops = [];
    private readonly StateStore _state;
    private readonly ObsClient _obs;
    private readonly Updater _updater;
    private IDisposable? _painter;
    private Dispatcher? _dispatcher;
    private HotkeySource? _hotkeys;
    private SystemHotkeys? _systemHotkeys;
    private SerialConsole? _console;

    private Composition(Log log, StateStore state, ObsClient obs, Updater updater)
    {
        Log = log;
        _state = state;
        _obs = obs;
        _updater = updater;
    }

    public Log Log { get; }

    public Tray? Tray { get; private set; }

    public Library Library { get; private set; } = null!;

    /// <summary>Lo que el programa sabe ahora. La prueba de humo lo lee: los problemas del estado incluyen los de los pasos, que la biblioteca sola no ve.</summary>
    public AppState Snapshot => _state.Current;

    public static Composition Build(string? root, SynchronizationContext ui) =>
        Build(
            DataRoot.LibraryFolder(root, AppIdentity.Dev, Environment.GetEnvironmentVariable, AppContext.BaseDirectory),
            ui,
            AppIdentity.LocalFolder,
            headless: false);

    /// <summary>
    /// La forma sin bandeja es la de la prueba de humo, que además usa una carpeta local propia y le pasa la biblioteca
    /// de ejemplo. Recibe la carpeta de la biblioteca ya resuelta; <c>null</c> es "sin biblioteca".
    /// </summary>
    internal static Composition Build(string? libraryFolder, SynchronizationContext ui, string localFolder, bool headless)
    {
        var clock = TimeProvider.System;
        var log = new Log(Path.Combine(localFolder, "log"), clock);
        var settings = new LocalSettings(Path.Combine(localFolder, "local.json"), log);
        settings.Load();    // si falla ya quedó en el registro, y sigue con los valores por defecto

        // Después de settings.Load(): es el lugar de "raíz de datos" y "Library.Load" de 01 §4.3. Sin carpeta compone igual y queda "sin biblioteca".
        var library = new Library(libraryFolder ?? "", Path.Combine(localFolder, "backup"));
        library.Load();
        log.Info("app", $"Biblioteca: {(library.Folder.Length == 0 ? "sin carpeta elegida" : library.Folder)}; estado {library.Status}; problemas {library.Problems.Count}");

        var state = new StateStore(log);

        // Antes de las cadenas: lights y la perilla le copian cada comando a las luces de la PC (plan de la 1.6.0, decisión 2).
        var openRgb = new OpenRgbClient();
        var pcLights = new PcLights(openRgb, settings, state, log, clock);

        // OBS: la dirección y la clave salen del archivo del propio OBS en cada intento (03 §2). El programa no guarda la clave.
        // "¿Hay proceso obs64?" separa closed, no_websocket, lost y exiting (03 §2): lo contesta IWindows (01 §3.1).
        var windows = new Win32Windows();
        var obsFiles = new ObsFiles(ObsFiles.DefaultFolder);
        var obs = new ObsClient(obsFiles.Address, obsFiles.Password, state, log, clock, () => windows.IsProcessRunning("obs64"));

        // El Dispatcher publica la biblioteca en el estado al construirse y en cada recarga, junto con los problemas de los pasos y de los atajos.
        // La salida en vivo de una prueba de la ventana: script.run la escribe y ComposeBridge la publica como script.output (etapa 8).
        // lights escribe por la consola, que se arma más abajo: también llega tarde, por la composición (etapa 8b).
        Dispatcher? dispatcher = null;
        Composition? composition = null;
        // La perilla en brightness manda por el mismo camino que lights (plan de la 1.3.0, decisión 7); page.overlay, como page.go, al Dispatcher.
        var output = new ScriptOutputSink();
        Func<string, CancellationToken, Task<Result>> sendToConsole = (line, ct) => composition!.SendToConsoleAsync(line, ct);
        (IReadOnlyList<IStepType> catalog, IReadOnlyList<ConditionType> conditions, Executor executor, ScriptRunner scripts) =
            ComposeChains(library, state, obs, windows, log, clock, target => dispatcher!.Go(target), output, sendToConsole, target => dispatcher!.ToggleOverlay(target), pcLights.Apply);
        var preflight = new Preflight(conditions, library, state, log, clock);

        // El umbral de mantener sale de console.holdMs de la biblioteca actual en cada gesto; si no está, es un segundo (02 §4.10).
        // En vivo: lo cambia Ajustes y rige sin reiniciar (plan de la 9b, decisión 12). El doble toque, igual: moduleKey.doubleTap (plan de la 1.3.0).
        var gestures = new Gestures(clock, () => Gestures.HoldFrom(library.Current.Console?.HoldMs), Gestures.DefaultFreshness, Gestures.ModuleKeyDoubleTap(() => library.Current));
        dispatcher = new Dispatcher(library, state, executor, gestures, log, sendToConsole, pcLights.Apply);
        var updater = new Updater(state, settings, log, clock);
        composition = new Composition(log, state, obs, updater);
        composition.Library = library;
        composition._dispatcher = dispatcher;
        (composition._pcLights, composition._openRgb) = (pcLights, openRgb);
        composition.StepTypes = catalog;
        composition.ConditionTypes = conditions;

        // Va acá y no en su lugar exacto de 01 §4.3, entre las fuentes de entrada y el Updater: sus dos canales necesitan la bandeja, que cuelga de composition.
        var notifier = new Notifier(
            state,
            library,
            log,
            playSound: notice => ui.Post(_ => composition.Tray?.PlayFailed(), null),
            showBalloon: notice => ui.Post(_ => composition.Tray?.ShowBalloon(notice.Title, notice.Text, notice.Warning), null),
            clock);
        dispatcher.Finished += notifier.OnFinished;
        composition.ComposeAlerts(state, library, preflight, dispatcher, notifier, log, clock);
        if (!headless && (settings.Current.HotkeysEnabled ?? !AppIdentity.Dev))
        {
            // Los atajos son globales. La copia de desarrollo no los toma, para no sacárselos a la instalada (05 §10);
            // se encienden a mano con "hotkeysEnabled": true en su local.json. La prueba de humo tampoco los toma.
            var systemHotkeys = new SystemHotkeys();
            var hotkeys = new HotkeySource(systemHotkeys, state, log);
            dispatcher.Attach(hotkeys);
            composition._systemHotkeys = systemHotkeys;
            composition._hotkeys = hotkeys;
        }

        composition.ComposeConsole(settings.Current.ConsolePort, state, dispatcher, log, clock, headless);

        // El puente con todos sus pedidos, después de las fuentes de entrada y el Notifier (01 §4.3). Fuera del if de la bandeja:
        // la prueba de humo comprueba que cada pedido tenga manejador (01 §3.2).
        // Las últimas corridas de cada script salen de las líneas de cierre del registro (etapa 8): se lee antes de que corra ninguno.
        var scriptsSources = new ScriptsSources(
            library, scripts, new ScriptRuns(log, Path.Combine(localFolder, "log"), clock), new ShellDialogs(ui, () => composition._window), library.BackupFolder, clock);
        // Lo de esta PC de Ajustes, también sin ventana (etapa 8). En una copia sin instalar no hay arranque ni actualización que tocar.
        var startup = new StartWithWindows(updater.IsInstalled);
        var settingsSources = new SettingsSources(
            settings,
            startup,
            updater.IsInstalled ? updater.CheckAsync : null,
            updater.IsInstalled ? updater.SkipAsync : null,
            () => new SettingsView(
                composition._hotkeys is not null,
                settings.Current.ConsolePort,
                settings.Current.SkippedVersion,
                SettingsView.Startup(startup.IsEnabled),
                new ObsSettingsView(obsFiles.Address().Authority),
                settings.McpAllowed(),
                InstallLayout.McpCommand(AppContext.BaseDirectory),
                PcLightsChoice.Of(settings.Current.PcLights)),
            SerialPorts.List,
            pcLights.SettingsChanged);
        composition.ComposeBridge(state, library, dispatcher, executor, catalog, conditions, windows, ui, localFolder, scriptsSources, output, settingsSources, preflight);

        if (!headless)
        {
            // La excepción entera, con su tipo y su pila: con sólo el mensaje no se sabe dónde pasó (hallazgo de E1-3).
            Application.ThreadException += (_, e) =>
                log.Error("app", "Excepción sin atender en la interfaz: " + e.Exception, ErrorCodes.InternalError);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                // Fuera de la interfaz (un hilo del grupo, una devolución de WebView2) el proceso se cae enseguida: se vacía el registro antes.
                log.Error("app", "Excepción sin atender fuera de la interfaz: " + e.ExceptionObject, ErrorCodes.InternalError);
                log.FlushAsync().Wait(TimeSpan.FromSeconds(1));
            };

            System.Action? onCheck = null;
            System.Action? onInstall = null;
            if (updater.IsInstalled)
            {
                onCheck = composition.CheckUpdate;
                onInstall = composition.InstallUpdate;
            }

            var tray = new Tray($"{AppIdentity.Title} {AppIdentity.Version}", onCheck, onInstall);
            composition.Tray = tray;
            tray.MenuOpened += notifier.Dismiss;
            tray.OpenWindowRequested += () => composition.OpenWindow(EditorWindow.FirstScreen);
            tray.StatusRequested += () => composition.OpenWindow(EditorWindow.StatusScreen);
            tray.ExitRequested += composition.RequestExit;
            composition.HookChains(tray, preflight, notifier, dispatcher);
            composition._pipe = new PipeListener(composition.Bridge, log, PipeProtocol.Name(AppIdentity.Dev), AppIdentity.Version);
            tray.ReconnectRequested += (_, _) => obs.Reconnect();
            tray.ConsoleToggleRequested += () =>
                composition._console?.Release(state.Current.Console.State != ConsoleLinkState.Released);

            // Leer el archivo puede tardar (un disco de red, un antivirus): fuera del hilo de la interfaz, y sin dejar escapar una excepción.
            tray.ReloadRequested += () => _ = Task.Run(() =>
            {
                try
                {
                    var problems = library.Load();
                    log.Info("app", $"Biblioteca recargada: estado {library.Status}; problemas {problems.Count}");
                }
                catch (Exception ex)
                {
                    log.Error("app", "Falló recargar la biblioteca: " + ex.Message, ErrorCodes.InternalError);
                }
            });
            tray.ChooseFolderRequested += () =>
            {
                if (FirstRun.AskRoot() is not null)
                {
                    MessageBox.Show("Listo. Salí de PampaPad y volvé a abrirlo para usar la carpeta nueva.", AppIdentity.Title);
                }
            };

            // Un solo pintor. Si llegan varios avisos antes de pintar, todos leen state.Current: se pinta el último.
            void Repaint()
            {
                var now = state.Current;
                tray.Paint(TraySummary.From(now));
                tray.PaintUpdate(TraySummary.UpdateItems(now));
            }

            Repaint();
            composition._painter = state.Subscribe((_, _) => ui.Post(_ => Repaint(), null));
        }

        log.Info("app", $"{AppIdentity.Title} {AppIdentity.Version}: compuesto, con datos en {localFolder}");
        return composition;
    }

    public Task StartAsync()
    {
        _loops.Add(_obs.KeepConnectedAsync(_life.Token));
        _loops.Add(_updater.KeepCheckingAsync(_life.Token));
        if (_hotkeys is not null)
        {
            _loops.Add(_hotkeys.StartAsync(_life.Token));
        }

        StartConsole();
        StartPcLights();

        if (_dispatcher is not null)
        {
            // El reloj de los gestos: sin él, un mantenido sólo saldría con el próximo estado que mande la placa.
            _loops.Add(_dispatcher.KeepTickingAsync(TimeProvider.System, _life.Token));
        }

        if (_pipe is not null)
        {
            _loops.Add(_pipe.RunAsync(_life.Token));
        }

        if (Tray is not null)
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        // Lo que es de la interfaz se suelta primero y en su hilo, antes del primer await.
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _painter?.Dispose();
        CloseWindow();
        Tray?.Dispose();
        _life.Cancel();
        _dispatcher?.CancelAll();
        try
        {
            await Task.WhenAll(_loops).WaitAsync(ShutdownLimit).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Log.Warning("app", "Los bucles no terminaron en 3 s; se sale igual");
        }
        catch (Exception ex)
        {
            Log.Error("app", "Un bucle terminó con error: " + ex.Message, ErrorCodes.InternalError);
        }

        // El orden de 01 §4.3: cancelar, esperar los bucles, soltar los atajos y recién después cerrar OBS.
        if (_hotkeys is not null)
        {
            await _hotkeys.DisposeAsync().ConfigureAwait(false);
        }

        _systemHotkeys?.Dispose();
        await DisposeConsoleAsync().ConfigureAwait(false);
        await DisposePcLightsAsync().ConfigureAwait(false);

        await _obs.DisposeAsync().ConfigureAwait(false);
        await _state.DisposeAsync().ConfigureAwait(false);
        Log.Info("app", "Salida ordenada");
        await Log.DisposeAsync().ConfigureAwait(false);
        _life.Dispose();
    }

    // Al volver de una suspensión el socket con OBS puede quedar abierto de un solo lado (01 §9): se fuerza el reintento.
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        Log.Info("app", "la PC volvió de la suspensión: se rearma la conexión con OBS");
        _obs.Reconnect();
    }
}
