using System.Collections.Concurrent;
using PampaPad.Core;
using CoreLibrary = PampaPad.Core.Library;

namespace PampaPad.Tests;

public class DispatcherTests
{
    private const string Short = "Ctrl+Shift+Alt+F9";
    private const string Inicio = "Ctrl+Shift+Alt+F6";
    private const string Fin = "Ctrl+Shift+Alt+F7";

    private const string WithOtherSteps = """
        {
          "version": 1,
          "pages": [ { "id": "p", "name": "P", "keys": [
            { "position": 1, "label": "Banco", "action": "escena", "hotkey": "Ctrl+Alt+B" },
            { "position": 2, "label": "Cuidada", "action": "cuidada", "hotkey": "Ctrl+Alt+C" },
            { "position": 3, "label": "Vacía", "hotkey": "Ctrl+Alt+V" },
            { "position": 5, "label": "Short", "action": "marca-short", "hotkey": "Ctrl+Shift+Alt+F9" }
          ] } ],
          "actions": [
            { "id": "escena", "name": "Escena", "steps": [ { "type": "obs.scene", "scene": "Banco" } ] },
            { "id": "cuidada", "name": "Cuidada", "steps": [ { "type": "obs.marker", "marker": "short", "onlyIf": { "type": "recording.is", "state": "recording" } } ] },
            { "id": "marca-short", "name": "Marca: short", "steps": [ { "type": "obs.marker", "marker": "short" } ] },
            { "id": "marca-largo", "name": "Marca: largo", "steps": [ { "type": "obs.marker", "marker": "largo" } ] }
          ],
          "hotkeys": [ { "hotkey": "Ctrl+Shift+Alt+F5", "action": "marca-largo" } ]
        }
        """;

    private static string Example => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "example-library", "config.json"));

    private static string Broken => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "config.broken.json"));

    [Fact]
    public async Task The_hotkey_of_a_key_runs_its_action()
    {
        await using var rig = new Rig(Example);

        Assert.True(rig.Windows.Press(Short));
        await Poll.Until(() => rig.Finished.Count == 1);

        ActionResult result = Assert.Single(rig.Finished);
        Assert.True(result.Ok);
        Assert.Equal("marca-short", result.ActionId);
        Assert.Equal(Trigger.Hotkey, result.RequestedBy);
        Assert.Equal("short", Assert.Single(rig.Marker.Runs).Parameters["marker"].GetString());
        Assert.Contains(rig.Log.Recent(), entry => entry.RunId == result.RunId && entry.RequestedBy == Trigger.Hotkey);
    }

    [Fact]
    public async Task A_general_hotkey_runs_its_action()
    {
        await using var rig = new Rig(WithOtherSteps);

        rig.Windows.Press("Ctrl+Shift+Alt+F5");
        await Poll.Until(() => rig.Finished.Count == 1);

        Assert.Equal("marca-largo", Assert.Single(rig.Finished).ActionId);
    }

    [Fact]
    public async Task The_same_action_does_not_overlap()
    {
        await using var rig = new Rig(Example);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Marker.Behavior = async (_, _, _) =>
        {
            await release.Task;
            return new StepResult(null);
        };

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Marker.Runs.Count == 1);
        RunningAction running = Assert.Single(rig.State.Current.Running);
        rig.Windows.Press(Short);
        ActionResult ignored = await rig.Dispatcher.Launch("marca-short", new Invocation(Guid.NewGuid(), Trigger.Tray, null, null, null, null));
        release.SetResult();
        await Poll.Until(() => rig.Finished.Count == 1);
        await Poll.Until(() => rig.State.Current.Running.Count == 0);

        Assert.Single(rig.Marker.Runs);
        Assert.Single(rig.Finished);
        Assert.Equal("marca-short", running.ActionId);
        Assert.False(ignored.Ok);
        Assert.Equal(ErrorCodes.Cancelled, ignored.Failure?.Code);
        Assert.Equal(2, rig.Log.Recent().Count(entry => entry.RunId == running.RunId && entry.Message.Contains("ya está en curso", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Two_presses_in_a_row_are_two_runs()
    {
        await using var rig = new Rig(Example);

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 1);
        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 2);

        Assert.Equal(2, rig.Marker.Runs.Count);
        Assert.All(rig.Finished, result => Assert.True(result.Ok));
        Assert.Equal(2, rig.Finished.Select(result => result.RunId).Distinct().Count());
    }

    [Fact]
    public async Task Two_different_actions_run_at_the_same_time()
    {
        await using var rig = new Rig(Example);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Marker.Behavior = async (_, _, _) =>
        {
            await release.Task;
            return new StepResult(null);
        };

        rig.Windows.Press(Short);
        rig.Windows.Press(Inicio);
        await Poll.Until(() => rig.Marker.Runs.Count == 2);

        Assert.Equal(new[] { "marca-inicio", "marca-short" }, rig.State.Current.Running.Select(run => run.ActionId).Order());
        release.SetResult();
        await Poll.Until(() => rig.Finished.Count == 2);
    }

    [Fact]
    public async Task A_mistyped_action_breaks_only_its_key()
    {
        await using var rig = new Rig(Broken);

        Problem problem = Assert.Single(rig.State.Current.Problems);
        rig.Windows.Press(Short);
        rig.Windows.Press(Fin);
        await Poll.Until(() => rig.Finished.Count == 2);
        rig.Windows.Press(Inicio);
        await Poll.Until(() => rig.Finished.Count == 3);

        Assert.Equal("pages[0].keys[1].action", problem.Where);
        Assert.Equal(2, rig.Marker.Runs.Count);
        ActionResult failed = Assert.Single(rig.Finished, result => !result.Ok);
        Assert.Equal(ErrorCodes.ConfigInvalid, failed.Failure?.Code);
        Assert.Contains("marca-inicoi", failed.Failure?.Message);
    }

    [Fact]
    public async Task An_unknown_step_type_and_an_onlyIf_are_problems_of_the_load_and_do_not_run()
    {
        await using var rig = new Rig(WithOtherSteps);

        Assert.Equal(
            new[] { ("actions[0].steps[0].type", ProblemCodes.StepTypeUnknown), ("actions[1].steps[0].onlyIf", ProblemCodes.ConditionUnknown) },
            rig.State.Current.Problems.Select(problem => (problem.Where, problem.Code)));

        rig.Windows.Press("Ctrl+Alt+B");
        rig.Windows.Press("Ctrl+Alt+C");
        await Poll.Until(() => rig.Finished.Count == 2);

        Assert.All(rig.Finished, result => Assert.Equal(ErrorCodes.ConfigInvalid, result.Failure?.Code));
        Assert.Empty(rig.Marker.Runs);
        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 3);
        Assert.Single(rig.Marker.Runs);
    }

    [Fact]
    public async Task A_key_without_action_does_nothing()
    {
        await using var rig = new Rig(WithOtherSteps);

        Assert.True(rig.Windows.Press("Ctrl+Alt+V"));

        Assert.Empty(rig.State.Current.Running);
        Assert.Empty(rig.Finished);
    }

    [Fact]
    public async Task What_windows_rejects_shows_up_with_its_place_in_the_file()
    {
        await using var rig = new Rig(Example, windows => windows.Taken.Add(Hotkey.Parse(Inicio)!.Value));

        Problem problem = Assert.Single(rig.State.Current.Problems);

        Assert.Equal("pages[0].keys[1].hotkey", problem.Where);
        Assert.Equal(ErrorCodes.HotkeyTaken, problem.Code);
        Assert.Equal(new HotkeysState(4, 1), rig.State.Current.Hotkeys);
    }

    [Fact]
    public async Task Reloading_takes_the_new_hotkeys_and_releases_the_old_ones()
    {
        await using var rig = new Rig(Example);

        rig.Write(Example.Replace(Short, "Ctrl+Shift+Alt+F10", StringComparison.Ordinal));
        rig.Library.Load();

        Assert.False(rig.Windows.Press(Short));
        Assert.True(rig.Windows.Press("Ctrl+Shift+Alt+F10"));
        await Poll.Until(() => rig.Finished.Count == 1);
    }

    [Fact]
    public async Task A_library_that_cannot_be_read_leaves_no_hotkeys_and_says_why()
    {
        await using var rig = new Rig(Example);

        rig.Write("{ roto");
        rig.Library.Load();

        Assert.Empty(rig.Windows.Registered);
        Assert.Equal(LibraryStatus.Unreadable, rig.State.Current.Library.Status);
        Assert.Equal(ErrorCodes.ConfigUnreadable, Assert.Single(rig.State.Current.Problems).Code);
        Assert.Equal("", rig.State.Current.BasePage);
    }

    [Fact]
    public async Task The_base_page_is_the_main_page_also_with_a_take_in_progress()
    {
        await using var rig = new Rig(Example);

        Assert.Equal("grabacion", rig.State.Current.BasePage);
        Assert.Equal("grabacion", rig.State.Current.ActivePage);
        Assert.Equal(LibraryStatus.Loaded, rig.State.Current.Library.Status);

        rig.State.Update(s => s with { Obs = s.Obs with { Recording = RecordingState.Recording } });
        rig.Dispatcher.Refresh();

        Assert.Equal("grabacion", rig.State.Current.BasePage);
        Assert.Equal(5, rig.Windows.Registered.Count);
    }

    [Fact]
    public async Task Cancelling_stops_what_is_running()
    {
        await using var rig = new Rig(Example);
        rig.Marker.Behavior = async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new StepResult(null);
        };

        rig.Windows.Press(Short);
        rig.Windows.Press(Inicio);
        await Poll.Until(() => rig.Marker.Runs.Count == 2);
        Guid first = rig.State.Current.Running.First(run => run.ActionId == "marca-short").RunId;

        Assert.False(rig.Dispatcher.Cancel(Guid.NewGuid()));
        Assert.True(rig.Dispatcher.Cancel(first));
        await Poll.Until(() => rig.Finished.Count == 1);
        rig.Dispatcher.CancelAll();
        await Poll.Until(() => rig.Finished.Count == 2);

        Assert.All(rig.Finished, result => Assert.Equal(ErrorCodes.Cancelled, result.Failure?.Code));
    }

    [Fact]
    public async Task A_subscriber_that_throws_does_not_break_the_next_press()
    {
        await using var rig = new Rig(Example);
        rig.Dispatcher.Finished += _ => throw new InvalidOperationException("roto a propósito");

        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 1);
        await Poll.Until(() => rig.State.Current.Running.Count == 0);
        rig.Windows.Press(Short);
        await Poll.Until(() => rig.Finished.Count == 2);

        // El Enqueue del Rig sube Finished.Count antes de que el suscriptor que tira desenrolle y el catch escriba la línea: se espera la línea.
        await Poll.Until(() => rig.Log.Recent().Any(entry => entry.Code == ErrorCodes.InternalError && entry.Message.Contains("roto a propósito", StringComparison.Ordinal)));
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly TempFolder _temp = new();

        public Rig(string config, Action<FakeSystemHotkeys>? before = null)
        {
            Directory.CreateDirectory(_temp.Combine("biblioteca"));
            Write(config);
            Log = new Log(_temp.Combine("log"), TimeProvider.System);
            Library = new CoreLibrary(_temp.Combine("biblioteca"), _temp.Combine("backup"));
            Library.Load();
            before?.Invoke(Windows);
            Dispatcher = new Dispatcher(Library, State, new Executor([Marker], TimeProvider.System), new Gestures(TimeProvider.System, Gestures.HoldFrom(null), Gestures.DefaultFreshness), Log);
            Dispatcher.Finished += Finished.Enqueue;
            Hotkeys = new HotkeySource(Windows, State, Log);
            _ = Hotkeys.StartAsync(CancellationToken.None);
            Dispatcher.Attach(Hotkeys);
        }

        public StateStore State { get; } = new();

        public Log Log { get; }

        public FakeStep Marker { get; } = new("obs.marker");

        public FakeSystemHotkeys Windows { get; } = new();

        public CoreLibrary Library { get; }

        public Dispatcher Dispatcher { get; }

        public HotkeySource Hotkeys { get; }

        public ConcurrentQueue<ActionResult> Finished { get; } = new();

        public void Write(string config) => File.WriteAllText(_temp.Combine("biblioteca", "config.json"), config);

        public async ValueTask DisposeAsync()
        {
            Dispatcher.CancelAll();
            await Hotkeys.DisposeAsync();
            await State.DisposeAsync();
            await Log.DisposeAsync();
            _temp.Dispose();
        }
    }
}
