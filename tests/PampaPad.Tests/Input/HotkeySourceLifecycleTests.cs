using System.Collections.Concurrent;
using System.Text.Json;
using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class HotkeySourceLifecycleTests : HotkeySourceFixture
{
    // ---- Presiones tardías y cierre

    [Fact]
    public void A_press_queued_before_the_page_changed_does_not_fire_the_new_key()
    {
        Source.Apply(PageOf("""{ "position": 5, "label": "A", "hotkey": "Ctrl+Alt+A" }"""), []);
        int oldId = Assert.Single(Windows.Registered.Keys);

        Source.Apply(PageOf("""{ "position": 1, "label": "B", "hotkey": "Ctrl+Alt+A" }"""), []);
        int newId = Assert.Single(Windows.Registered.Keys);

        Windows.PressId(oldId);
        Assert.Empty(Events);
        Assert.NotEqual(oldId, newId);
        Windows.PressId(newId);
        Assert.Equal("key.1", Assert.Single(Events).Control);
    }

    [Fact]
    public async Task A_press_that_arrives_while_the_old_combinations_are_being_released_is_dropped()
    {
        var windows = new RecordingHotkeys();
        var events = new List<InputEvent>();
        var source = new HotkeySource(windows, State, Log);
        source.Input += events.Add;
        await source.StartAsync(CancellationToken.None);
        source.Apply(PageOf("""{ "position": 5, "label": "A", "hotkey": "Ctrl+Alt+A" }"""), []);
        windows.OnUnregister = windows.Raise;

        source.Apply(new Page(), []);

        Assert.Empty(events);
    }

    [Fact]
    public async Task Listening_starts_once_and_stops_when_the_source_is_closed()
    {
        Assert.Equal(1, Windows.Listeners);
        await Source.StartAsync(CancellationToken.None);
        Assert.Equal(1, Windows.Listeners);

        await Source.DisposeAsync();
        Assert.Equal(0, Windows.Listeners);

        await Source.DisposeAsync();
        await Source.StartAsync(CancellationToken.None);
        Assert.Equal(0, Windows.Listeners);
    }

    [Fact]
    public async Task After_closing_a_late_press_and_a_late_apply_do_nothing()
    {
        Source.Apply(PageOf(Marks), []);
        int id = Windows.Registered.Keys.First();
        await Source.DisposeAsync();

        Windows.PressId(id);
        IReadOnlyList<Problem> problems = Source.Apply(PageOf(Marks), General("""{ "hotkey": "Ctrl+Alt+S", "action": "a" }"""));

        Assert.Empty(Events);
        Assert.Empty(problems);
        Assert.Empty(Windows.Registered);
    }

    // ---- Quien escucha falla

    [Fact]
    public void A_listener_that_throws_does_not_escape_and_is_logged_and_the_next_press_still_arrives()
    {
        Source.Input += _ => throw new InvalidOperationException("se rompió");
        Source.Apply(PageOf(Marks), []);

        Exception? escaped = Record.Exception(() => Windows.Press("Ctrl+Shift+Alt+F9"));
        Windows.Press("Ctrl+Shift+Alt+F6");

        Assert.Null(escaped);
        Assert.Equal(new[] { "key.5", "key.6" }, Events.Select(input => input.Control));
        LogEntry entry = Assert.Single(Log.Recent(), candidate => candidate.Level == LogLevel.Error && candidate.Message.Contains("key.5", StringComparison.Ordinal));
        Assert.Equal("hotkeys", entry.Module);
        Assert.Equal(ErrorCodes.InternalError, entry.Code);
        Assert.Contains("se rompió", entry.Message);
    }

    // ---- Ids de RegisterHotKey

    [Fact]
    public void Ids_stay_inside_what_windows_accepts_and_wrap_without_meeting_a_live_one()
    {
        var windows = new RecordingHotkeys();
        var source = new HotkeySource(windows, State, Log);
        List<HotkeyBinding> every = [];
        for (int modifiers = 1; modifiers <= 7; modifiers++)
        {
            foreach (int key in Enumerable.Range('A', 26).Concat(Enumerable.Range('0', 10)).Concat(Enumerable.Range(0x70, 24)))
            {
                every.Add(new HotkeyBinding { Hotkey = new Hotkey((HotkeyModifiers)modifiers, key).ToString(), Action = "a" });
            }
        }

        // 420 combinaciones distintas por pasada: 118 pasadas pasan de los 49 151 ids que admite Windows.
        for (int pass = 0; pass < 118; pass++)
        {
            Assert.Empty(source.Apply(new Page(), every));
        }

        Assert.Equal(420, windows.Live);
        Assert.Equal(118 * 420, windows.Requested.Count);
        Assert.All(windows.Requested, id => Assert.InRange(id, 1, 0xBFFF));
        Assert.Equal(0xBFFF, windows.Requested.Max());
        Assert.Contains(windows.Requested.Zip(windows.Requested.Skip(1)), pair => pair.Second < pair.First);
    }

    [Fact]
    public async Task Presses_from_another_thread_while_the_page_changes_never_fail()
    {
        var windows = new RecordingHotkeys();
        var seen = new ConcurrentBag<InputEvent>();
        var source = new HotkeySource(windows, State, Log);
        source.Input += seen.Add;
        await source.StartAsync(CancellationToken.None);
        Page a = PageOf("""{ "position": 1, "label": "A", "hotkey": "Ctrl+Alt+A" }""");
        Page b = PageOf("""{ "position": 2, "label": "B", "hotkey": "Ctrl+Alt+B" }""");
        using var finished = new CancellationTokenSource();

        Task presser = Task.Run(() =>
        {
            int id = 0;
            while (!finished.IsCancellationRequested)
            {
                windows.Raise(1 + (id++ % 12));
            }
        });
        Task applier = Task.Run(() =>
        {
            for (int i = 0; i < 2000; i++)
            {
                source.Apply(i % 2 == 0 ? a : b, []);
            }
        });

        await applier;
        await finished.CancelAsync();
        await presser;

        Assert.All(seen, input => Assert.True(input.Control is "key.1" or "key.2"));
        Assert.Equal(1, windows.Live);
        await source.DisposeAsync();
        Assert.Equal(0, windows.Live);
    }

    // ---- Lo que publica y lo que lee

    [Fact]
    public void A_fresh_state_has_no_hotkeys()
    {
        Assert.Equal(new HotkeysState(0, 0), new AppState().Hotkeys);
        Assert.Equal(new HotkeysState(0, 0), State.Current.Hotkeys);
    }

    [Fact]
    public void The_switch_of_this_pc_is_written_back_as_it_was_read_and_left_out_when_it_has_no_value()
    {
        string off = JsonSerializer.Serialize(new LocalData { HotkeysEnabled = false }, Json.Options);
        string unset = JsonSerializer.Serialize(new LocalData(), Json.Options);

        Assert.Contains("\"hotkeysEnabled\": false", off, StringComparison.Ordinal);
        Assert.DoesNotContain("hotkeysEnabled", unset, StringComparison.Ordinal);
        Assert.True(JsonSerializer.Deserialize<LocalData>("""{ "version": 1, "hotkeysEnabled": true }""", Json.Options)!.HotkeysEnabled);
    }

    // ---- Lo que tiene que valer para el RegisterHotKey real

    [Theory]
    [InlineData("Alt+A", 0x4001u)]
    [InlineData("Ctrl+A", 0x4002u)]
    [InlineData("Shift+A", 0x4004u)]
    [InlineData("Ctrl+Alt+A", 0x4003u)]
    [InlineData("Alt+Shift+A", 0x4005u)]
    [InlineData("Ctrl+Shift+A", 0x4006u)]
    [InlineData("Ctrl+Shift+Alt+F24", 0x4007u)]
    public void Every_modifier_maps_to_its_native_value(string text, uint expected)
    {
        Assert.Equal(expected, HotkeyNative.Modifiers(Parse(text)));
    }

    [Theory]
    [InlineData("key.", false)]
    [InlineData("key. 1", false)]
    [InlineData("key.1 ", false)]
    [InlineData("key.+1", false)]
    [InlineData("key.-1", false)]
    [InlineData("key.1.5", false)]
    [InlineData("key.99999999999", false)]
    [InlineData("KEY.1", false)]
    [InlineData("key.一", false)]
    [InlineData("", false)]
    [InlineData("key.9", true)]
    public void Only_a_plain_key_dot_number_is_a_key(string control, bool isKey)
    {
        Assert.Equal(isKey, Controls.TryKey(control, out int position));
        if (!isKey)
        {
            Assert.Equal(0, position);
        }
    }
}
