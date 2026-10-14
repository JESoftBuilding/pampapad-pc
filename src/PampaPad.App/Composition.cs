using System.Diagnostics;
using Microsoft.Win32;
using PampaPad.Core;

namespace PampaPad.App;

/// <summary>Cómo se llama esta copia. En Debug lleva el sufijo -dev para convivir con la instalada (05 §10).</summary>
internal static class AppIdentity
{
#if DEBUG
    public const string Name = "PampaPad-dev";
    public const string Title = "PampaPad (dev)";
    public const bool Dev = true;
#else
    public const string Name = "PampaPad";
    public const string Title = "PampaPad";
    public const bool Dev = false;
#endif

    public static string Version { get; } = typeof(AppIdentity).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string LocalFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);
}

/// <summary>La única raíz de composición: todos los new, en el orden de 01 §4.3, y el apagado en orden inverso.</summary>
internal sealed class Composition : IAsyncDisposable
{
    private static readonly TimeSpan ShutdownLimit = TimeSpan.FromSeconds(3);

    private readonly CancellationTokenSource _life = new();
    private readonly List<Task> _loops = [];
    private readonly StateStore _state;
    private readonly ObsClient _obs;
    private readonly Updater _updater;
    private IDisposable? _painter;

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

        // Provisorio: en E3-7 lo reemplaza el Dispatcher, que además junta los problemas de los pasos y de los atajos.
        void PublishLibrary() => state.Update(s => s with
        {
            Library = new LibraryState(library.Status, library.Folder),
            Problems = library.Problems,
        });
        library.Changed += _ => PublishLibrary();
        PublishLibrary();

        // OBS: la dirección y la clave salen del archivo del propio OBS en cada intento (03 §2). El programa no guarda la clave.
        var obsFiles = new ObsFiles(ObsFiles.DefaultFolder);
        var obs = new ObsClient(obsFiles.Address, obsFiles.Password, state, log, clock, ObsIsRunning);
        var updater = new Updater(state, settings, log, clock);
        var composition = new Composition(log, state, obs, updater);
        composition.Library = library;
        if (!headless)
        {
            Application.ThreadException += (_, e) =>
                log.Error("app", "Excepción sin atender en la interfaz: " + e.Exception.Message, ErrorCodes.InternalError);

            System.Action? onCheck = null;
            System.Action? onInstall = null;
            if (updater.IsInstalled)
            {
                onCheck = composition.CheckUpdate;
                onInstall = composition.InstallUpdate;
            }

            var tray = new Tray($"{AppIdentity.Title} {AppIdentity.Version}", onCheck, onInstall);
            composition.Tray = tray;
            tray.ReconnectRequested += (_, _) => obs.Reconnect();

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
        if (Tray is not null)
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        return Task.CompletedTask;
    }

    public void OpenRequested() => Log.Info("app", "Otra copia pidió abrir la ventana; todavía no hay ventana");

    public async ValueTask DisposeAsync()
    {
        // Lo que es de la interfaz se suelta primero y en su hilo, antes del primer await.
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _painter?.Dispose();
        Tray?.Dispose();
        _life.Cancel();
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

    // "¿Hay proceso obs64?" separa closed, no_websocket, lost y exiting (03 §2). Cuando exista IWindows.IsProcessRunning, se usa esa.
    private static bool ObsIsRunning()
    {
        var found = Process.GetProcessesByName("obs64");
        foreach (var process in found)
        {
            process.Dispose();
        }

        return found.Length > 0;
    }

    private void CheckUpdate() => _ = RunAsync("buscar una actualización", () => _updater.CheckAsync(_life.Token));

    private void InstallUpdate() => _ = RunAsync("instalar la actualización", async () =>
    {
        var result = await _updater.DownloadAndApplyAsync(_life.Token);
        if (result.Ok)
        {
            // Seguimos en el hilo de la interfaz. Velopack ya espera a que el programa salga para instalarlo y reabrirlo.
            Tray?.ExitThread();
        }

        return result;
    });

    /// <summary>Los manejadores del menú no pueden dejar escapar una excepción ni quedar sin anotar.</summary>
    private async Task RunAsync(string what, Func<Task<Result>> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Log.Error("app", $"Falló {what}: {ex.Message}", ErrorCodes.InternalError);
        }
    }
}
