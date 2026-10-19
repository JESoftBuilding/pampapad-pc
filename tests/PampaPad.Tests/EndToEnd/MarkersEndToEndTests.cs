using System.Collections.Concurrent;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public class MarkersEndToEndTests
{
    private static string ExampleLibrary => Path.Combine(AppContext.BaseDirectory, "Data", "example-library");

    [Theory]
    [InlineData("Ctrl+Shift+Alt+F5", "largo")]
    [InlineData("Ctrl+Shift+Alt+F6", "inicio")]
    [InlineData("Ctrl+Shift+Alt+F7", "fin")]
    [InlineData("Ctrl+Shift+Alt+F8", "sacar")]
    [InlineData("Ctrl+Shift+Alt+F9", "short")]
    public async Task A_hotkey_leaves_a_chapter_with_its_type(string hotkey, string title)
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        await using var rig = new Rig(session);

        Assert.Empty(session.State.Current.Problems);
        Assert.True(rig.Windows.Press(hotkey));
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.True(Assert.Single(rig.Finished).Ok);
        Assert.Equal(new[] { title }, session.Obs.Chapters);
        Assert.Null(session.State.Current.LastFailure);
        Assert.Empty(rig.Sounds);
    }

    [Fact]
    public async Task The_five_in_a_row_leave_five_chapters_in_order()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Recording);
        await using var rig = new Rig(session);

        foreach (string hotkey in new[] { "Ctrl+Shift+Alt+F5", "Ctrl+Shift+Alt+F6", "Ctrl+Shift+Alt+F7", "Ctrl+Shift+Alt+F8", "Ctrl+Shift+Alt+F9" })
        {
            int before = rig.Finished.Count;
            rig.Windows.Press(hotkey);
            await Poll.Until(() => rig.Finished.Count == before + 1);
        }

        Assert.Equal(new[] { "largo", "inicio", "fin", "sacar", "short" }, session.Obs.Chapters);
    }

    [Fact]
    public async Task In_pause_the_marker_does_not_go_in_and_only_the_icon_says_so()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Paused);
        await using var rig = new Rig(session);

        rig.Windows.Press("Ctrl+Shift+Alt+F7");
        await Poll.Until(() => rig.Finished.Count == 1);

        ActionResult result = Assert.Single(rig.Finished);
        Assert.Equal(ErrorCodes.MarkerPaused, result.Failure?.Code);
        Assert.Equal(1, result.StepNumber);
        Assert.Empty(session.Obs.Chapters);
        Assert.Equal(
            new ActionFailure("marker_failed", "Falló Marca: fin: paso 1 (obs.marker)", "Estás en pausa: la marca no entró."),
            session.State.Current.LastFailure);
        Assert.Equal(TrayIcon.ActionFailed, TraySummary.From(session.State.Current).Icon);
        Assert.Empty(rig.Sounds);
        Assert.Empty(rig.Balloons);
    }

    [Fact]
    public async Task Stopped_the_marker_does_not_go_in_and_it_sounds_and_shows_the_balloon()
    {
        await using ObsSession session = await ObsSession.StartAsync(RecordingState.Stopped);
        await using var rig = new Rig(session);

        rig.Windows.Press("Ctrl+Shift+Alt+F9");
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Equal(ErrorCodes.MarkerNotRecording, Assert.Single(rig.Finished).Failure?.Code);
        Assert.Equal("No estás grabando: la marca no entró.", Assert.Single(rig.Balloons).Text);
        Assert.Single(rig.Sounds);
    }

    /// <summary>Lo mismo que arma Composition para las marcas, con Windows de mentira.</summary>
    private sealed class Rig : IAsyncDisposable
    {
        private readonly HotkeySource _hotkeys;

        public Rig(ObsSession session)
        {
            var library = new CoreLibrary(ExampleLibrary, Path.Combine(Path.GetTempPath(), "pampapad-e2e-backup"));
            library.Load();
            var executor = new Executor(Catalog.Build(session.Client, session.State, session.Log, TimeProvider.System, TestChains.For(library, session.Log)), TimeProvider.System);
            var dispatcher = new Dispatcher(library, session.State, executor, new Gestures(TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness), session.Log);
            var notifier = new Notifier(session.State, library, session.Log, Sounds.Enqueue, Balloons.Enqueue);
            dispatcher.Finished += notifier.OnFinished;
            dispatcher.Finished += Finished.Enqueue;
            _hotkeys = new HotkeySource(Windows, session.State, session.Log);
            _ = _hotkeys.StartAsync(CancellationToken.None);
            dispatcher.Attach(_hotkeys);
        }

        public FakeSystemHotkeys Windows { get; } = new();

        public ConcurrentQueue<ActionResult> Finished { get; } = new();

        public ConcurrentQueue<Notice> Sounds { get; } = new();

        public ConcurrentQueue<Notice> Balloons { get; } = new();

        public ValueTask DisposeAsync() => _hotkeys.DisposeAsync();
    }
}
