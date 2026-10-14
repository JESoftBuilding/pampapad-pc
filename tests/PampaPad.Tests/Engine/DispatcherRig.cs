using System.Collections.Concurrent;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>
/// El despachador armado como lo arma Composition, con un paso obs.marker de mentira, atajos de Windows de mentira y una biblioteca
/// en una carpeta temporal. Sin config es "sin carpeta elegida". Las pruebas de bordes comparten esto; las del plan tienen su Rig.
/// </summary>
internal sealed class DispatcherRig : IAsyncDisposable
{
    public const string Short = "Ctrl+Shift+Alt+F9";
    public const string Inicio = "Ctrl+Shift+Alt+F6";

    private readonly TempFolder _temp = new();

    public DispatcherRig(string? config, Action<FakeSystemHotkeys>? beforeWindows = null, Action<FakeStep>? beforeDispatcher = null, bool attach = true)
    {
        Directory.CreateDirectory(_temp.Combine("biblioteca"));
        if (config is not null)
        {
            Write(config);
        }

        Log = new Log(_temp.Combine("log"), TimeProvider.System);
        Library = new CoreLibrary(config is null ? "" : _temp.Combine("biblioteca"), _temp.Combine("backup"));
        Library.Load();
        beforeWindows?.Invoke(Windows);
        beforeDispatcher?.Invoke(Marker);
        Dispatcher = new Dispatcher(Library, State, new Executor([Marker], TimeProvider.System), Log);
        Dispatcher.Finished += Finished.Enqueue;
        Hotkeys = new HotkeySource(Windows, State, Log);
        _ = Hotkeys.StartAsync(CancellationToken.None);
        if (attach)
        {
            Dispatcher.Attach(Hotkeys);
        }
    }

    public static string Example => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "example-library", "config.json"));

    public StateStore State { get; } = new();

    public Log Log { get; }

    public FakeStep Marker { get; } = new("obs.marker");

    public FakeSystemHotkeys Windows { get; } = new();

    public CoreLibrary Library { get; }

    public Dispatcher Dispatcher { get; }

    public HotkeySource Hotkeys { get; }

    public ConcurrentQueue<ActionResult> Finished { get; } = new();

    /// <summary>
    /// Las líneas de error del despachador y de lo que arma. No cuenta el aviso del propio registro de que no pudo escribir en el disco
    /// (módulo "log"): un antivirus que toma el archivo en un mal momento no es una falla del despachador.
    /// </summary>
    public IReadOnlyList<LogEntry> Errors => [.. Log.Recent().Where(entry => entry.Level == LogLevel.Error && entry.Module != "log")];

    public void Write(string config) => File.WriteAllText(_temp.Combine("biblioteca", "config.json"), config);

    /// <summary>Un paso que se queda esperando hasta que se llame a lo que devuelve.</summary>
    public System.Action Hold()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Marker.Behavior = async (_, _, _) =>
        {
            await release.Task;
            return new StepResult(null);
        };
        return () => release.TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        Dispatcher.CancelAll();
        await Hotkeys.DisposeAsync();
        await State.DisposeAsync();
        await Log.DisposeAsync();
        _temp.Dispose();
    }
}
