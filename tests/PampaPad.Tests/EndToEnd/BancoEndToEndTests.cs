using System.Collections.Concurrent;
using PampaPad.Core;
using PampaPad.Tests.Doubles;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

/// <summary>
/// "Ir al banco" de la biblioteca de ejemplo de punta a punta (01 §8, fila 5): el atajo o la tecla 3 de la placa, el script, la espera de
/// la ventana y la escena. El bancocam de estas pruebas no toca el celular: deja un archivo, y la ventana de mentira aparece cuando lo ve.
/// </summary>
public class BancoEndToEndTests
{
    private const string Key3Down = "#301110BAAAAAAAAAA=\n";

    private const string Harmless = "@echo off\r\necho listo> \"%~dp0abierto.txt\"\r\necho bancocam corriendo\r\n";

    private static readonly WindowInfo Mirror = new("bancocam", "scrcpy", 1823, 850, false);

    [Fact]
    public async Task By_hotkey_it_opens_the_mirror_waits_for_its_window_and_puts_banco_on_air()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session, Harmless);
        Assert.NotEqual(FakeObs.BancoUuid, session.State.Current.Obs.SceneUuid);

        Assert.True(rig.Hotkeys.Press("Ctrl+Shift+Alt+B"));
        ActionResult result = await rig.NextAsync();

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal("ir-al-banco", result.ActionId);
        Assert.Equal(Trigger.Hotkey, result.RequestedBy);
        Assert.Equal(new[] { StepOutcome.Ok, StepOutcome.Ok, StepOutcome.Ok }, result.Steps.Select(step => step.Outcome));
        Assert.Equal("bancocam corriendo", result.Steps[0].Message);
        Assert.True(File.Exists(rig.Marker));
        Assert.Equal(FakeObs.BancoUuid, session.State.Current.Obs.SceneUuid);
    }

    [Fact]
    public async Task Key_3_of_the_board_does_the_same()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session, Harmless);
        var cable = new MemoryPipe();
        await using var console = new SerialConsole(_ => Task.FromResult<Stream>(cable), session.State, session.Log, TimeProvider.System);
        rig.Dispatcher.Attach(console);
        await console.StartAsync(CancellationToken.None);

        cable.Feed(Key3Down);
        ActionResult result = await rig.NextAsync();

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal(("ir-al-banco", Trigger.Key), (result.ActionId, result.RequestedBy));
        Assert.Equal(FakeObs.BancoUuid, session.State.Current.Obs.SceneUuid);
    }

    [Fact]
    public async Task With_the_mirror_already_open_the_script_is_skipped_and_banco_goes_on_air()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session, Harmless);
        rig.Windows.Open(Mirror);

        Assert.True(rig.Hotkeys.Press("Ctrl+Shift+Alt+B"));
        ActionResult result = await rig.NextAsync();

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal(new[] { StepOutcome.Omitted, StepOutcome.Ok, StepOutcome.Ok }, result.Steps.Select(step => step.Outcome));
        Assert.False(File.Exists(rig.Marker));
        Assert.Equal(FakeObs.BancoUuid, session.State.Current.Obs.SceneUuid);
    }

    [Fact]
    public async Task Without_the_phone_the_chain_stops_at_the_script_with_its_last_line_and_the_scene_stays()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session, "@echo off\r\necho scrcpy no abrio la ventana (celu conectado?)\r\nexit /b 1\r\n");
        string? before = session.State.Current.Obs.SceneUuid;

        Assert.True(rig.Hotkeys.Press("Ctrl+Shift+Alt+B"));
        ActionResult result = await rig.NextAsync();

        Assert.False(result.Ok);
        Assert.Equal((1, "script.run"), (result.StepNumber, result.StepType));
        Assert.Equal(ErrorCodes.ScriptFailed, result.Failure!.Code);
        Assert.Equal("El script \"bancocam\" salió con 1: scrcpy no abrio la ventana (celu conectado?)", result.Failure.Message);
        Assert.Equal(new[] { StepOutcome.Failed, StepOutcome.NotRun, StepOutcome.NotRun }, result.Steps.Select(step => step.Outcome));
        Assert.Equal(before, session.State.Current.Obs.SceneUuid);
    }

    /// <summary>Lo que arma Composition, con la biblioteca de ejemplo en una carpeta temporal y un bancocam.cmd que no toca el celular.</summary>
    private sealed class Rig : IAsyncDisposable
    {
        private readonly TempFolder _temp = new();
        private readonly HotkeySource _hotkeySource;
        private readonly ConcurrentQueue<ActionResult> _finished = new();

        public Rig(ObsSession session, string bancocam)
        {
            string folder = _temp.Combine("biblioteca");
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "example-library", "config.json"), Path.Combine(folder, "config.json"));
            TestChains.CopyExampleScripts(folder);
            File.WriteAllText(Path.Combine(folder, "scripts", "bancocam", "bancocam.cmd"), bancocam);
            Marker = Path.Combine(folder, "scripts", "bancocam", "abierto.txt");
            Windows.OpenWhen(() => File.Exists(Marker), Mirror);

            var library = new CoreLibrary(folder, _temp.Combine("backup"));
            library.Load();
            Dispatcher? dispatcher = null;
            Chains chains = TestChains.For(library, session.Log, Windows, target => dispatcher!.Go(target));
            var executor = new Executor(
                Catalog.Build(session.Client, session.State, session.Log, TimeProvider.System, chains),
                Conditions.Build(Windows, session.State, session.Client),
                TimeProvider.System);
            dispatcher = new Dispatcher(library, session.State, executor, new Gestures(TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness), session.Log);
            Dispatcher = dispatcher;
            Dispatcher.Finished += _finished.Enqueue;
            _hotkeySource = new HotkeySource(Hotkeys, session.State, session.Log);
            _ = _hotkeySource.StartAsync(CancellationToken.None);
            Dispatcher.Attach(_hotkeySource);
            Assert.Empty(session.State.Current.Problems);
        }

        public string Marker { get; }

        public FakeWindows Windows { get; } = new();

        public FakeSystemHotkeys Hotkeys { get; } = new();

        public Dispatcher Dispatcher { get; }

        public async Task<ActionResult> NextAsync()
        {
            await Poll.Until(() => !_finished.IsEmpty, 20_000);
            Assert.True(_finished.TryDequeue(out ActionResult? result));
            return result;
        }

        public async ValueTask DisposeAsync()
        {
            Dispatcher.CancelAll();
            await _hotkeySource.DisposeAsync();
            _temp.Dispose();
        }
    }
}
