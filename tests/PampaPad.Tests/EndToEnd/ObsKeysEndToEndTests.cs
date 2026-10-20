using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using PampaPad.Core;
using PampaPad.Tests.Doubles;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public class ObsKeysEndToEndTests
{
    private static string Example => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "example-library", "config.json"));

    [Fact]
    public async Task The_example_loads_clean_with_the_real_catalog_and_takes_its_nine_hotkeys()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session, Example);

        Assert.Empty(session.State.Current.Problems);
        Assert.Equal(new HotkeysState(9, 0), session.State.Current.Hotkeys);      // B y M en sus teclas; parar, grabar y las cinco marcas en hotkeys
        Assert.Equal("grabacion", session.State.Current.BasePage);
    }

    [Fact]
    public async Task Record_and_stop_by_keyboard_follow_obs()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session, Example);

        Assert.True(rig.Windows.Press("Ctrl+Shift+Alt+G"));
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording && rig.Finished.Count == 1);
        Assert.True(rig.Windows.Press("Ctrl+Shift+Alt+D"));
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Stopped && rig.Finished.Count == 2);

        Assert.All(rig.Finished, result => Assert.True(result.Ok));
        Assert.Equal(1, session.Obs.Times("StartRecord"));
        Assert.Equal(1, session.Obs.Times("StopRecord"));
    }

    [Fact]
    public async Task The_mic_hotkey_toggles_the_mic()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session, Example);

        Assert.True(rig.Windows.Press("Ctrl+Shift+Alt+M"));
        await Poll.Until(() => session.State.Current.Obs.MicMuted && rig.Finished.Count == 1);
        Assert.True(rig.Windows.Press("Ctrl+Shift+Alt+M"));
        await Poll.Until(() => !session.State.Current.Obs.MicMuted && rig.Finished.Count == 2);

        Assert.False(session.Obs.MicMuted);
    }

    [Fact]
    public async Task On_the_console_a_tap_on_12_records_and_holding_it_stops()
    {
        var clock = new FakeTimeProvider();
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session, Example, clock);

        rig.Dispatcher.Receive(new InputEvent("console", "key.12", Gesture.Press));
        rig.Dispatcher.Receive(new InputEvent("console", "key.12", Gesture.Release));        // toque: grabar
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording && rig.Finished.Count == 1);

        rig.Dispatcher.Receive(new InputEvent("console", "key.12", Gesture.Press));           // mantener un segundo: parar
        for (int i = 0; i < 10; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            rig.Dispatcher.Receive(new InputEvent("console", "key.12", Gesture.Press));
        }

        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Stopped && rig.Finished.Count == 2);
        Assert.Equal(new[] { "grabar", "parar" }, rig.Finished.Select(result => result.ActionId));
    }

    [Theory]
    [InlineData(3, FakeObs.BancoUuid)]
    [InlineData(6, "5bcf806a-9dac-430c-a437-3eab544e8419")]
    [InlineData(12, "ed2f1482-848c-4500-840d-807aee465d1f")]
    public async Task With_the_scenes_page_as_the_main_one_each_key_puts_its_scene_on_air(int position, string uuid)
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session, Example.Replace("\"mainPage\": \"grabacion\"", "\"mainPage\": \"escenas\"", StringComparison.Ordinal));

        rig.Dispatcher.Receive(new InputEvent("console", Controls.Key(position), Gesture.Press));
        await Poll.Until(() => session.State.Current.Obs.SceneUuid == uuid);

        Assert.Equal("escenas", session.State.Current.BasePage);
    }

    [Fact]
    public async Task From_the_scenes_page_a_scene_goes_on_air_and_with_the_take_stopped_brings_back_the_main_page()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session, Example);
        Assert.Null(rig.Dispatcher.Go("escenas"));

        rig.Dispatcher.Receive(new InputEvent("console", Controls.Key(12), Gesture.Press));
        await Poll.Until(() => rig.Finished.Count == 1);

        ActionResult result = Assert.Single(rig.Finished);
        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal(new[] { StepOutcome.Ok, StepOutcome.Ok }, result.Steps.Select(step => step.Outcome));
        // La escena del estado la pone el aviso de OBS, que puede llegar después de que la acción terminó.
        await Poll.Until(() => session.State.Current.Obs.SceneUuid == "ed2f1482-848c-4500-840d-807aee465d1f");
        Assert.Equal("grabacion", session.State.Current.BasePage);
        Assert.Equal(new HotkeysState(9, 0), session.State.Current.Hotkeys);
    }

    [Fact]
    public async Task Recording_a_scene_from_the_window_skips_the_page_change_and_goes_on_air()
    {
        await using ObsSession session = await ObsSession.StartAsync();
        await using var rig = new Rig(session, Example);
        Assert.True(rig.Windows.Press("Ctrl+Shift+Alt+G"));
        await Poll.Until(() => session.State.Current.Obs.Recording == RecordingState.Recording && rig.Finished.Count == 1);

        ActionResult result = await rig.Dispatcher.Launch("escena-fin", new Invocation(Guid.NewGuid(), Trigger.Window, null, null, null, null));

        Assert.True(result.Ok, result.Failure?.Message);
        Assert.Equal(new[] { StepOutcome.Omitted, StepOutcome.Ok }, result.Steps.Select(step => step.Outcome));
        // La escena del estado la pone el aviso de OBS, que puede llegar después de que la acción terminó.
        await Poll.Until(() => session.State.Current.Obs.SceneUuid == "ed2f1482-848c-4500-840d-807aee465d1f");
        Assert.Equal("grabacion", session.State.Current.BasePage);
    }

    /// <summary>Lo que arma Composition para OBS, con Windows de mentira y la biblioteca en una carpeta temporal.</summary>
    private sealed class Rig : IAsyncDisposable
    {
        private readonly TempFolder _temp = new();
        private readonly HotkeySource _hotkeys;

        public Rig(ObsSession session, string config, TimeProvider? clock = null)
        {
            Directory.CreateDirectory(_temp.Combine("biblioteca"));
            File.WriteAllText(_temp.Combine("biblioteca", "config.json"), config);
            TestChains.CopyExampleScripts(_temp.Combine("biblioteca"));
            var library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
            library.Load();
            Dispatcher? dispatcher = null;
            Chains chains = TestChains.For(library, session.Log, goToPage: target => dispatcher!.Go(target));
            var executor = new Executor(
                Catalog.Build(session.Client, session.State, session.Log, TimeProvider.System, chains),
                Conditions.Build(chains.Windows, session.State, session.Client),
                TimeProvider.System);
            var gestures = new Gestures(clock ?? TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness);
            dispatcher = new Dispatcher(library, session.State, executor, gestures, session.Log);
            Dispatcher = dispatcher;
            Dispatcher.Finished += Finished.Enqueue;
            _hotkeys = new HotkeySource(Windows, session.State, session.Log);
            _ = _hotkeys.StartAsync(CancellationToken.None);
            Dispatcher.Attach(_hotkeys);
        }

        public FakeSystemHotkeys Windows { get; } = new();

        public Dispatcher Dispatcher { get; }

        public ConcurrentQueue<ActionResult> Finished { get; } = new();

        public async ValueTask DisposeAsync()
        {
            Dispatcher.CancelAll();
            await _hotkeys.DisposeAsync();
            _temp.Dispose();
        }
    }
}
