using PampaPad.Core;

namespace PampaPad.App;

/// <summary>Cómo se llama esta copia. En Debug lleva el sufijo -dev para convivir con la instalada (05 §10).</summary>
internal static class AppIdentity
{
#if DEBUG
    public const string Name = "PampaPad-dev";
    public const string Title = "PampaPad (dev)";
#else
    public const string Name = "PampaPad";
    public const string Title = "PampaPad";
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
    private readonly Updater _updater;
    private IDisposable? _painter;

    private Composition(Log log, StateStore state, Updater updater)
    {
        Log = log;
        _state = state;
        _updater = updater;
    }

    public Log Log { get; }

    public Tray? Tray { get; private set; }

    public static Composition Build(string? root, SynchronizationContext ui) =>
        Build(root, ui, AppIdentity.LocalFolder, headless: false);

    /// <summary>La forma sin bandeja es la de la prueba de humo, que además usa una carpeta local propia.</summary>
    internal static Composition Build(string? root, SynchronizationContext ui, string localFolder, bool headless)
    {
        var clock = TimeProvider.System;
        var log = new Log(Path.Combine(localFolder, "log"), clock);
        var settings = new LocalSettings(Path.Combine(localFolder, "local.json"), log);
        settings.Load();    // si falla ya quedó en el registro, y sigue con los valores por defecto
        var state = new StateStore(log);
        var updater = new Updater(state, settings, log, clock);
        var composition = new Composition(log, state, updater);
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
            composition._painter = state.Subscribe((_, _) => ui.Post(_ => tray.Paint(state.Current), null));
        }

        log.Info("app", $"{AppIdentity.Title} {AppIdentity.Version}: compuesto, con datos en {localFolder}");
        return composition;
    }

    public Task StartAsync()
    {
        _loops.Add(_updater.KeepCheckingAsync(_life.Token));
        return Task.CompletedTask;
    }

    public void OpenRequested() => Log.Info("app", "Otra copia pidió abrir la ventana; todavía no hay ventana");

    public async ValueTask DisposeAsync()
    {
        // Lo que es de la interfaz se suelta primero y en su hilo, antes del primer await.
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

        await _state.DisposeAsync().ConfigureAwait(false);
        Log.Info("app", "Salida ordenada");
        await Log.DisposeAsync().ConfigureAwait(false);
        _life.Dispose();
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
