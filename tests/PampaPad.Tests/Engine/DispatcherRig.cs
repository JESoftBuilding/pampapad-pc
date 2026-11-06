using System.Collections.Concurrent;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>
/// El despachador armado como lo arma Composition, con un paso de mentira por cada tipo de la biblioteca de ejemplo, las dos condiciones que
/// usa (siempre se cumplen), atajos de Windows de mentira y una biblioteca en una carpeta temporal. Sin config es "sin carpeta elegida".
/// Las pruebas de bordes comparten esto; las del plan tienen su Rig.
/// </summary>
internal sealed class DispatcherRig : IAsyncDisposable
{
    public const string Short = "Ctrl+Shift+Alt+F9";
    public const string Inicio = "Ctrl+Shift+Alt+F6";

    private readonly TempFolder _temp = new();

    /// <summary>send es el SendToConsole que recibe el despachador: el puerto falso por el que la perilla manda el brillo.</summary>
    public DispatcherRig(
        string? config,
        Action<FakeSystemHotkeys>? beforeWindows = null,
        Action<FakeStep>? beforeDispatcher = null,
        bool attach = true,
        TimeProvider? clock = null,
        Func<string, CancellationToken, Task<Result>>? send = null)
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
        // page.overlay es el de verdad: lo único que hace es pedirle al despachador que ponga o saque la página encima.
        var steps = new IStepType[] { Marker, Scene, Record, Mute, Request, Script, WaitWindow, Page, new PageOverlay(Library, target => Dispatcher!.ToggleOverlay(target)) };
        ConditionType[] conditions = [FakeCondition.Met(Conditions.WindowExists).Type, FakeCondition.Met(Conditions.RecordingIs).Type];
        Executor = new Executor(steps, conditions, TimeProvider.System);
        Dispatcher = new Dispatcher(Library, State, Executor, new Gestures(clock ?? TimeProvider.System, () => Gestures.HoldFrom(null), Gestures.DefaultFreshness, Gestures.ModuleKeyDoubleTap(() => Library.Current)), Log, send);
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

    public FakeStep Scene { get; } = new("obs.scene");

    public FakeStep Record { get; } = new("obs.record");

    public FakeStep Mute { get; } = new("obs.mute");

    public FakeStep Request { get; } = new("obs.request");

    public FakeStep Script { get; } = new("script.run");

    public FakeStep WaitWindow { get; } = new("wait.window");

    public FakeStep Page { get; } = new("page.go");

    public FakeSystemHotkeys Windows { get; } = new();

    public CoreLibrary Library { get; }

    public Executor Executor { get; }

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
