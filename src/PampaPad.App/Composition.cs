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

    private Composition(Log log) => Log = log;

    public Log Log { get; }

    public Tray? Tray { get; private set; }

    public static Composition Build(string? root, SynchronizationContext ui) =>
        Build(root, ui, AppIdentity.LocalFolder, headless: false);

    /// <summary>La forma sin bandeja es la de la prueba de humo, que además usa una carpeta local propia.</summary>
    internal static Composition Build(string? root, SynchronizationContext ui, string localFolder, bool headless)
    {
        var clock = TimeProvider.System;
        var log = new Log(Path.Combine(localFolder, "log"), clock);
        var composition = new Composition(log);
        if (!headless)
        {
            Application.ThreadException += (_, e) =>
                log.Error("app", "Excepción sin atender en la interfaz: " + e.Exception.Message, ErrorCodes.InternalError);
            composition.Tray = new Tray($"{AppIdentity.Title} {AppIdentity.Version}");
        }

        log.Info("app", $"{AppIdentity.Title} {AppIdentity.Version}: compuesto, con datos en {localFolder}");
        return composition;
    }

    public Task StartAsync() => Task.CompletedTask;

    public void OpenRequested() => Log.Info("app", "Otra copia pidió abrir la ventana; todavía no hay ventana");

    public async ValueTask DisposeAsync()
    {
        // Lo que es de la interfaz se suelta primero y en su hilo, antes del primer await.
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

        Log.Info("app", "Salida ordenada");
        await Log.DisposeAsync().ConfigureAwait(false);
        _life.Dispose();
    }
}
